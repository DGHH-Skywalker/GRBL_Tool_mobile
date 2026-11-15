using Android.App;
using Android.Content;
using Android.OS;
using Android.Widget;
using LaserMobile.Domain;
using LaserMobile.Extensions.Abstractions;
using LaserMobile.Imaging.Skia;
using System.Globalization;
using System.Text;
namespace LaserMobile.AndroidApp;

public sealed partial class MainActivity
{
    private string? pendingExportFile, pendingExportName;
    private int pendingImportRequest, nextImportRequest = 200;
    private Task ChooseImport()
    {
        if (importing || converting || pendingExportFile is not null)
        { ShowInfo("请先完成或取消当前作品处理"); return Task.CompletedTask; }
        // A launcher return can leave an OEM picker outstanding. A new explicit import replaces it.
        if (pendingImport is not null)
        {
            int old = pendingImportRequest; pendingImport = null; pendingImportRequest = 0;
#pragma warning disable CA1422
            FinishActivity(old);
#pragma warning restore CA1422
        }
        ++artworkLoadGeneration;
        Choices("导入作品", ["位图 · PNG / JPG / WebP / BMP", "矢量 · SVG", "加工文件 · NC / G-code / GC / NGC / TAP / CNC"], index =>
            OpenPicker(index switch { 0 => ImportKind.Bitmap, 1 => ImportKind.Svg, _ => ImportKind.Gcode }));
        return Task.CompletedTask;
    }
    private Task OpenPicker(ImportKind kind)
    {
        if (importing || converting || pendingImport is not null || pendingExportFile is not null) throw new InvalidOperationException("请先完成或取消当前导入、导出或生成");
        pendingImport = kind; pendingImportRequest = nextImportRequest = nextImportRequest >= 32000 ? 200 : nextImportRequest + 1; ClearNotice();
        try
        {
            if (kind == ImportKind.Bitmap) LaunchPhotoPicker();
            else
            {
                var format = ImportFormats.Get(kind);
                using var intent = new Intent(Intent.ActionOpenDocument); intent.AddCategory(Intent.CategoryOpenable);
                intent.SetType(format.MimeTypes.Count == 1 ? format.MimeTypes[0] : "*/*");
                intent.PutExtra("android.provider.extra.INITIAL_URI", Android.Net.Uri.Parse("content://com.android.externalstorage.documents/document/primary%3ADownload"));
                intent.PutExtra(Intent.ExtraMimeTypes, format.MimeTypes.ToArray()); intent.PutExtra(Intent.ExtraAllowMultiple, false);
                LaunchPicker(intent, pendingImportRequest);
            }
        }
        catch { pendingImport = null; pendingImportRequest = 0; UpdateActionAvailability(); throw; }
        return Task.CompletedTask;
    }
    private void LaunchPhotoPicker()
    {
        // Android Photo Picker (including system backports), then the system gallery on older ROMs.
        // Bitmap selection never falls back to ACTION_OPEN_DOCUMENT / ACTION_GET_CONTENT.
        using var modern = new Intent("android.provider.action.PICK_IMAGES"); modern.SetType("image/*");
        if (modern.ResolveActivity(PackageManager!) is not null)
        {
            try { LaunchPicker(modern, pendingImportRequest); return; }
            catch (ActivityNotFoundException) { }
        }
        using var gallery = new Intent(Intent.ActionPick, Android.Provider.MediaStore.Images.Media.ExternalContentUri);
        gallery.SetType("image/*"); gallery.PutExtra(Intent.ExtraMimeTypes, ImportFormats.Get(ImportKind.Bitmap).MimeTypes.ToArray());
        gallery.PutExtra(Intent.ExtraAllowMultiple, false);
        if (gallery.ResolveActivity(PackageManager!) is null) throw new ActivityNotFoundException("系统相册不可用，请启用系统照片选择器或相册应用后重试");
        LaunchPicker(gallery, pendingImportRequest);
    }
#pragma warning disable CA1422
    private void LaunchPicker(Intent intent, int requestCode) => StartActivityForResult(intent, requestCode);
#pragma warning restore CA1422
    private async Task ExportDocument(int requestCode, string name)
    {
        if (pendingExportFile is not null || pendingImport is not null || importing) throw new InvalidOperationException("请先完成或取消当前文件选择");
        // Capture the clicked job/log now; returning from the system UI must not export a different selection.
        var job = requestCode == 21 ? selected ?? throw new InvalidOperationException("请先选择任务") : null;
        var snapshot = CurrentSession()?.Snapshot; var log = CurrentSession()?.Log ?? [];
        string file = Path.Combine(CacheDir!.AbsolutePath, "export-" + Guid.NewGuid().ToString("N") + ".txt");
        pendingExportFile = file; pendingExportName = name;
        try
        {
            await Task.Run(async () =>
            {
                string content = job is not null ? library.Read(job.Id).Content : "GRBL Tool Mobile 0.7.1\n" + (snapshot?.ToString() ?? "Disconnected") + "\n" + string.Join('\n', log);
                await File.WriteAllTextAsync(file, content, new UTF8Encoding(false));
            });
            if (IsDestroyed) { ClearPendingExport(); return; }
            using var intent = new Intent(Intent.ActionCreateDocument); intent.SetType("text/plain"); intent.AddCategory(Intent.CategoryOpenable); intent.PutExtra(Intent.ExtraTitle, name);
            intent.PutExtra("android.provider.extra.INITIAL_URI", Android.Net.Uri.Parse("content://com.android.externalstorage.documents/document/primary%3ADownload"));
            LaunchPicker(intent, requestCode);
        }
        catch { ClearPendingExport(); throw; }
    }
    private void ClearPendingExport()
    {
        string? file = pendingExportFile; pendingExportFile = pendingExportName = null;
        if (file is not null) { try { File.Delete(file); } catch (IOException) { } }
    }
    private async Task<ValidatedImport> ReadAndValidate(Android.Net.Uri uri, ImportKind kind)
    {
        if (uri.Scheme != "content") throw new FormatException("请选择系统相册中的图片或系统文档");
        var resolver = ContentResolver ?? throw new IOException("文件服务不可用");
        string name = ""; long? declaredSize = null;
        using (var cursor = resolver.Query(uri, ["_display_name", "_size"], null, null, null))
        {
            if (cursor?.MoveToFirst() == true)
            {
            int nameColumn = cursor.GetColumnIndex("_display_name"), sizeColumn = cursor.GetColumnIndex("_size");
            name = nameColumn >= 0 ? cursor.GetString(nameColumn) ?? "" : "";
            if (sizeColumn >= 0 && !cursor.IsNull(sizeColumn)) declaredSize = cursor.GetLong(sizeColumn);
            }
        }
        var mime = resolver.GetType(uri);
        if (kind == ImportKind.Bitmap) name = ImportFormats.PhotoName(name, mime);
        var format = ImportFormats.CheckMetadata(name, mime, kind);
        if (declaredSize is { } size && size > format.MaximumBytes) throw new FormatException($"文件超过 {format.MaximumBytes / 1000} KB 上限");
        using var stream = resolver.OpenInputStream(uri) ?? throw new IOException("无法读取文件");
        using var output = new MemoryStream(); byte[] buffer = new byte[8192]; int read;
        while ((read = await stream.ReadAsync(buffer)) != 0)
        {
            if (output.Length + read > format.MaximumBytes) throw new FormatException($"文件超过 {format.MaximumBytes / 1000} KB 上限");
            await output.WriteAsync(buffer.AsMemory(0, read));
        }
        return ImportValidator.Validate(output.ToArray(), name, mime, kind);
    }
#pragma warning disable CS0672, CA1422
    protected override async void OnActivityResult(int requestCode, Android.App.Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        bool isImport = pendingImport is not null && requestCode == pendingImportRequest;
        if (!isImport && requestCode is not (21 or 22)) return;
        var expected = pendingImport;
        if (isImport) { pendingImport = null; pendingImportRequest = 0; }
        var uri = data?.Data;
        if (uri is null && data?.ClipData is { ItemCount: 1 } clip) uri = clip.GetItemAt(0)?.Uri;
        if (resultCode != Android.App.Result.Ok)
        {
            if (!isImport) ClearPendingExport();
            ShowInfo(isImport ? "已取消导入，当前作品保持原样" : "已取消导出"); UpdateActionAvailability(); return;
        }
        if (uri is null)
        {
            if (!isImport) ClearPendingExport();
            ShowError("选择器未返回可读取的内容，请重新选择"); UpdateActionAvailability(); return;
        }
        string previous = artwork.Text ?? "";
        try
        {
            if (isImport)
            {
                if (expected is not { } kind) throw new FormatException("导入类型已失效，请重新选择文件");
                if (importing) throw new InvalidOperationException("文件正在检查");
                importing = true; UpdateActionAvailability(); artwork.Text = expected == ImportKind.Bitmap ? "正在检查相册图片…" : "正在检查文件…";
                var accepted = await Task.Run(() => ReadAndValidate(uri, kind));
                if (IsDestroyed) return;
                if (accepted.Kind == ImportKind.DeviceProfile)
                {
                    var preset = DevicePreset.Parse(new UTF8Encoding(false, true).GetString(accepted.Bytes).TrimStart('\ufeff'));
                    areaWidth.Text = preset.Width.ToString(CultureInfo.InvariantCulture); areaHeight.Text = preset.Height.ToString(CultureInfo.InvariantCulture);
                    maxFeed.Text = preset.MaxFeed.ToString(CultureInfo.InvariantCulture); maxPower.Text = preset.MaxPower.ToString(CultureInfo.InvariantCulture);
                    if (preset.Usb is { } usb) SaveUsbSettings(usb);
                    artwork.Text = previous; ShowMessage("配置已导入", preset.DisplayName + "\n请读取设备配置后核对"); return;
                }
                source = accepted.Bytes; sourceName = accepted.Name; selected = null; prepared = null; preview?.Dispose(); preview = null; imagePreparation = null; quarterTurns = 0; ClearNotice();
                InvalidateDraft(); zoom = 1; panX = panY = 0; artwork.Text = $"{sourceName}\n已检查 · {source.Length / 1000.0:0.#} KB";
                canvas.Invalidate(); jobCanvas?.Invalidate(); ShowPage(1); artworkTabs(0);
            }
            else if (requestCode is 21 or 22)
            {
                string file = pendingExportFile ?? throw new IOException("导出内容已失效，请重新点击导出");
                string name = pendingExportName ?? "文件";
                await Task.Run(async () =>
                {
                    using var output = ContentResolver!.OpenOutputStream(uri) ?? throw new IOException("无法写入文件");
                    await using var input = File.OpenRead(file); await input.CopyToAsync(output); await output.FlushAsync();
                });
                if (!IsDestroyed) ShowInfo("已导出 · " + name);
            }
        }
        catch (Exception e)
        {
            if (!IsDestroyed) { artwork.Text = previous; ShowError(e is System.Xml.XmlException or System.Text.Json.JsonException ? "文档内容无效，请检查所选文件" : e.Message); }
        }
        finally
        {
            if (isImport) importing = false; else ClearPendingExport();
            if (!IsDestroyed) UpdateActionAvailability();
        }
    }
#pragma warning restore CS0672, CA1422
}
