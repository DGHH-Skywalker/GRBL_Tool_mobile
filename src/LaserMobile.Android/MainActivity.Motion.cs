using Android.Views;
using Android.Views.Animations;
using Android.Provider;
namespace LaserMobile.AndroidApp;
public sealed partial class MainActivity
{
    private void Reveal(View view)
    {
        view.Animate()?.Cancel(); view.TranslationY = 0; view.Alpha = 1;
        if (!visible || Settings.Global.GetFloat(ContentResolver, Settings.Global.AnimatorDurationScale, 1) == 0) return;
        view.Alpha = 0; view.TranslationY = Dp(6);
        view.Animate()?.Alpha(1)?.TranslationY(0)?.SetDuration(160)?.SetInterpolator(new DecelerateInterpolator(1.5f))?.Start();
    }
    private static void ResetMotion(View view)
    { view.Animate()?.Cancel(); view.Alpha = 1; view.TranslationY = 0; }
}
