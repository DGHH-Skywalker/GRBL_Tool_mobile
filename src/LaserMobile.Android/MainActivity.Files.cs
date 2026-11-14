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
    private Task ChooseImport()
    {
        if (importing || converting) { ShowError("请先完成或取消当前图像处理"); return Task.CompletedTask; }
        Choices("导入作品", ["位图 · PNG / JPG / WebP / BMP", "矢量 · SVG", "加工文件 · NC / G-code / GC / NGC / TAP / CNC"], index =>
        { OpenPicker(index switch { 0 => ImportKind.Bitmap, 1 => ImportKind.Svg, _ => ImportKind.Gcode }); return Task.CompletedTask; });
        return Task.CompletedTask;
    }
    private void OpenPicker(ImportKind kind)
    {
        if (importing) throw new InvalidOperationException("文件正在检查，请稍候");
        var format = ImportFormats.Get(kind); pendingImport = kind;
        using var intent = new Intent(Intent.ActionOpenDocument); intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType(format.MimeTypes.Count == 1 ? format.MimeTypes[0] : "*/*");
        intent.PutExtra("android.provider.extra.INITIAL_URI", Android.Net.Uri.Parse("content://com.android.externalstorage.documents/document/primary%3ADownload"));
        intent.PutExtra(Intent.ExtraMimeTypes, format.MimeTypes.ToArray()); intent.PutExtra(Intent.ExtraAllowMultiple, false);
#pragma warning disable CA1422
        StartActivityForResult(intent, 20);
#pragma warning restore CA1422
    }
    private void ExportDocument(int requestCode, string name)
    {
        using var intent = new Intent(Intent.ActionCreateDocument); intent.SetType("text/plain"); intent.AddCategory(Intent.CategoryOpenable); intent.PutExtra(Intent.ExtraTitle, name);
#pragma warning disable CA1422
        StartActivityForResult(intent, requestCode);
#pragma warning restore CA1422
    }
    private async Task<ValidatedImport> ReadAndValidate(Android.Net.Uri uri, ImportKind kind)
    {
        if (uri.Scheme != "content") throw new FormatException("请选择系统文件选择器中的文档");
        var resolver = ContentResolver ?? throw new IOException("文件服务不可用");
        string name; long? declaredSize = null;
        using (var cursor = resolver.Query(uri, ["_display_name", "_size"], null, null, null))
        {
            if (cursor?.MoveToFirst() != true) throw new IOException("无法读取文件信息");
            int nameColumn = cursor.GetColumnIndex("_display_name"), sizeColumn = cursor.GetColumnIndex("_size");
            name = nameColumn >= 0 ? cursor.GetString(nameColumn) ?? "" : "";
            if (sizeColumn >= 0 && !cursor.IsNull(sizeColumn)) declaredSize = cursor.GetLong(sizeColumn);
        }
        var mime = resolver.GetType(uri); var format = ImportFormats.CheckMetadata(name, mime, kind);
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
        var expected = pendingImport;
        if (requestCode == 20) pendingImport = null;
        if (resultCode != Android.App.Result.Ok || data?.Data is not { } uri) return;
        string previous = artwork.Text ?? "";
        try
        {
            if (requestCode == 20)
            {
                if (expected is not { } kind) throw new FormatException("导入类型已失效，请重新选择文件");
                if (importing) throw new InvalidOperationException("文件正在检查");
                importing = true; artwork.Text = "正在检查文件…";
                var accepted = await Task.Run(() => ReadAndValidate(uri, kind));
                if (IsDestroyed) return;
                if (accepted.Kind == ImportKind.DeviceProfile)
                {
                    var preset = DevicePreset.Parse(new UTF8Encoding(false, true).GetString(accepted.Bytes).TrimStart('\ufeff'));
                    areaWidth.Text = preset.Width.ToString(CultureInfo.InvariantCulture); areaHeight.Text = preset.Height.ToString(CultureInfo.InvariantCulture);
                    maxFeed.Text = preset.MaxFeed.ToString(CultureInfo.InvariantCulture); maxPower.Text = preset.MaxPower.ToString(CultureInfo.InvariantCulture);
                    artwork.Text = previous; ShowMessage("配置已导入", preset.DisplayName + "\n请读取设备配置后核对"); return;
                }
                source = accepted.Bytes; sourceName = accepted.Name; selected = null; prepared = null; preview?.Dispose(); preview = null; imagePreparation = null; quarterTurns = 0; ClearNotice();
                InvalidateDraft(); zoom = 1; panX = panY = 0; artwork.Text = $"{sourceName}\n已检查 · {source.Length / 1000.0:0.#} KB";
                canvas.Invalidate(); jobCanvas?.Invalidate(); ShowPage(1); artworkTabs(0);
            }
            else if (requestCode is 21 or 22)
            {
                using var stream = ContentResolver!.OpenOutputStream(uri) ?? throw new IOException("无法写入文件");
                string content = requestCode == 21 ? library.Read(selected?.Id ?? throw new InvalidOperationException("任务未选择")).Content
                    : "GRBL Tool Mobile 0.6.0\n" + (CurrentSession()?.Snapshot.ToString() ?? "Disconnected") + "\n" + string.Join('\n', CurrentSession()?.Log ?? []);
                await stream.WriteAsync(Encoding.UTF8.GetBytes(content));
            }
        }
        catch (Exception e)
        {
            if (!IsDestroyed) { artwork.Text = previous; ShowError(e is System.Xml.XmlException or System.Text.Json.JsonException ? "文档内容无效，请检查所选文件" : e.Message); }
        }
        finally { importing = false; }
    }
#pragma warning restore CS0672, CA1422
}
