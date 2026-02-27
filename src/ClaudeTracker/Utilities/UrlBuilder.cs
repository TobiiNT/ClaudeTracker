namespace ClaudeTracker.Utilities;

public class UrlBuilder
{
    private readonly string _baseUrl;
    private string _path = string.Empty;

    public UrlBuilder(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public UrlBuilder AppendingPath(string path)
    {
        _path += "/" + path.Trim('/');
        return this;
    }

    public UrlBuilder AppendingPathComponents(IEnumerable<string> components)
    {
        foreach (var component in components)
        {
            _path += "/" + Uri.EscapeDataString(component.Trim('/'));
        }
        return this;
    }

    public Uri Build()
    {
        var fullUrl = _baseUrl + _path;
        if (!Uri.TryCreate(fullUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"Malformed URL: {fullUrl}");
        return uri;
    }
}
