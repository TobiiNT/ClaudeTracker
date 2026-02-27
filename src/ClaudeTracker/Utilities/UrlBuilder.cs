namespace ClaudeTracker.Utilities;

/// <summary>Fluent builder for constructing API endpoint URLs.</summary>
public class UrlBuilder
{
    private readonly string _baseUrl;
    private string _path = string.Empty;

    public UrlBuilder(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
    }

    /// <summary>Appends a path segment (trimming slashes).</summary>
    public UrlBuilder AppendingPath(string path)
    {
        _path += "/" + path.Trim('/');
        return this;
    }

    /// <summary>Appends multiple URL-encoded path components.</summary>
    public UrlBuilder AppendingPathComponents(IEnumerable<string> components)
    {
        foreach (var component in components)
        {
            _path += "/" + Uri.EscapeDataString(component.Trim('/'));
        }
        return this;
    }

    /// <summary>Builds the final URI, throwing if the result is malformed.</summary>
    public Uri Build()
    {
        var fullUrl = _baseUrl + _path;
        if (!Uri.TryCreate(fullUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"Malformed URL: {fullUrl}");
        return uri;
    }
}
