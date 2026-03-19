using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using ClaudeTracker.Services;

namespace ClaudeTracker.Views;

public partial class BrowserSignInWindow : Window
{
    private readonly string _targetUrl;
    private readonly string _cookieDomain;
    private readonly TaskCompletionSource<(string sessionKey, DateTime? expiry)?> _result = new();
    private bool _ssoRedirectAttempted;

    public Task<(string sessionKey, DateTime? expiry)?> ResultTask => _result.Task;

    /// <summary>
    /// JSON response from fetching /api/organizations within the WebView2 context.
    /// Available after ResultTask completes. Callers can parse this directly to bypass
    /// Cloudflare when making the same API call via HttpClient.
    /// </summary>
    public string? VerifiedOrganizationsJson { get; private set; }

    public BrowserSignInWindow(string targetUrl, string cookieDomain)
    {
        InitializeComponent();
        _targetUrl = targetUrl;
        _cookieDomain = cookieDomain;
        Loaded += OnLoaded;
        Closed += (_, _) => _result.TrySetResult(null);
    }

    public static bool IsWebView2Available()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return !string.IsNullOrEmpty(version);
        }
        catch { return false; }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(
                userDataFolder: Utilities.Constants.WebView2.ProfilePath);
            await WebView.EnsureCoreWebView2Async(env);
            WebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            Closed += (_, _) => WebView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
            WebView.CoreWebView2.Navigate(_targetUrl);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("WebView2 init failed", ex);
            StatusText.Text = "WebView2 initialization failed";
        }
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        try
        {
            // 1. Always check the target domain first — this is the cookie we actually need
            var targetCookies = await WebView.CoreWebView2.CookieManager.GetCookiesAsync(_targetUrl);
            foreach (var cookie in targetCookies)
            {
                if (cookie.Name == "sessionKey" && cookie.Domain.Contains(_cookieDomain))
                {
                    await CaptureAndClose(cookie);
                    return;
                }
            }

            // 2. Check current page URL — OAuth may have redirected to a different domain (e.g. claude.ai)
            var currentUrl = WebView.CoreWebView2.Source;
            var currentCookies = await WebView.CoreWebView2.CookieManager.GetCookiesAsync(currentUrl);
            foreach (var cookie in currentCookies)
            {
                if (cookie.Name == "sessionKey")
                {
                    // User authenticated on a different domain. Navigate to the target domain
                    // to trigger cross-domain SSO, which should set the target domain's cookie.
                    if (!_ssoRedirectAttempted)
                    {
                        _ssoRedirectAttempted = true;
                        StatusText.Text = "Completing sign-in...";
                        WebView.CoreWebView2.Navigate($"https://{_cookieDomain}");
                        return;
                    }

                    // SSO redirect already attempted — fall back to the cookie we have
                    await CaptureAndClose(cookie);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Cookie extraction failed", ex);
        }
    }

    private async Task CaptureAndClose(CoreWebView2Cookie cookie)
    {
        DateTime? expiry = cookie.Expires > DateTime.MinValue ? cookie.Expires.ToUniversalTime() : null;

        // Verify the connection via WebView2's fetch (which has Cloudflare cookies).
        // ExecuteScriptAsync does NOT await JS Promises, so we use postMessage to get
        // the async fetch result back to C#.
        StatusText.Text = "Verifying connection...";
        try
        {
            var tcs = new TaskCompletionSource<string?>();

            void OnMessage(object? s, CoreWebView2WebMessageReceivedEventArgs args)
            {
                WebView.CoreWebView2.WebMessageReceived -= OnMessage;
                tcs.TrySetResult(args.TryGetWebMessageAsString());
            }

            WebView.CoreWebView2.WebMessageReceived += OnMessage;

            await WebView.CoreWebView2.ExecuteScriptAsync("""
                fetch('/api/organizations', {
                    credentials: 'include',
                    headers: { 'Accept': 'application/json' }
                })
                .then(r => r.ok ? r.text() : '')
                .then(text => window.chrome.webview.postMessage(text))
                .catch(() => window.chrome.webview.postMessage(''))
            """);

            // Wait for the message with a timeout
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(10_000));
            if (completed == tcs.Task)
            {
                var response = await tcs.Task;
                if (!string.IsNullOrEmpty(response))
                    VerifiedOrganizationsJson = response;
            }
            else
            {
                WebView.CoreWebView2.WebMessageReceived -= OnMessage;
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogWarning($"WebView2 verification fetch failed: {ex.Message}");
        }

        StatusText.Text = "Session key captured!";
        _result.TrySetResult((cookie.Value, expiry));
        await Task.Delay(500);
        Dispatcher.Invoke(Close);
    }
}
