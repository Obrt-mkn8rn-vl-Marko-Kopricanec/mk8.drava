namespace Mk8.Drava.Application.BLL.ControlPlane.Routing;
public sealed class PathRewritePolicy
{
    public string Apply(PathRewritePolicyInput input, string target, string path)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(path);
        if (!string.IsNullOrWhiteSpace(input.StripPrefix) && path.StartsWith(input.StripPrefix, StringComparison.Ordinal))
        {
            return RewriteTarget(target, input.StripPrefix, "");
        }

        if (!string.IsNullOrWhiteSpace(input.ReplacePrefix) && path.StartsWith(input.ReplacePrefix, StringComparison.Ordinal))
        {
            return RewriteTarget(target, input.ReplacePrefix, input.Replacement);
        }

        return target;
    }

    private static string RewriteTarget(string target, string oldPrefix, string newPrefix)
    {
        var queryIndex = target.IndexOf('?', StringComparison.Ordinal);
        var path = queryIndex < 0 ? target : target[..queryIndex];
        var query = queryIndex < 0 ? "" : target[queryIndex..];
        var remainder = path[oldPrefix.Length..];
        var rewrittenPath = string.IsNullOrEmpty(newPrefix) ? remainder : newPrefix + remainder;
        if (string.IsNullOrEmpty(rewrittenPath))
        {
            rewrittenPath = "/";
        }

        if (!rewrittenPath.StartsWith('/'))
        {
            rewrittenPath = "/" + rewrittenPath;
        }

        return rewrittenPath + query;
    }
}
