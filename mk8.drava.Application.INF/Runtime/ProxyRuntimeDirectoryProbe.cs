using Mk8.Drava.Application.BLL.ControlPlane.RuntimePreflight;

namespace Mk8.Drava.Application.INF.Runtime;
public sealed class ProxyRuntimeDirectoryProbe : IProxyRuntimeDirectoryProbe
{
    public ProxyRuntimeDirectoryProbeResult Probe(string path, bool createIfMissing)
    {
        var created = false;
        try
        {
            if (!Directory.Exists(path))
            {
                if (!createIfMissing)
                {
                    return ProxyRuntimeDirectoryProbeResult.Missing();
                }

                Directory.CreateDirectory(path);
                created = true;
            }

            var canRead = CanRead(path);
            var canWrite = CanWrite(path);
            return ProxyRuntimeDirectoryProbeResult.Probed(created, canRead, canWrite);
        }
        catch (UnauthorizedAccessException)
        {
            return ProxyRuntimeDirectoryProbeResult.AccessDenied(Directory.Exists(path), created);
        }
        catch (IOException)
        {
            return ProxyRuntimeDirectoryProbeResult.IoError(Directory.Exists(path), created);
        }
    }

    private static bool CanRead(string path)
    {
        try
        {
            using var entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
            entries.MoveNext();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool CanWrite(string path)
    {
        var fileName = Path.Combine(path, $".mdrava-preflight-{Guid.NewGuid():N}.tmp");
        try
        {
            using (File.Create(fileName, 1, FileOptions.DeleteOnClose))
            {
            }

            if (File.Exists(fileName))
            {
                File.Delete(fileName);
            }

            return true;
        }
        catch
        {
            try
            {
                if (File.Exists(fileName))
                {
                    File.Delete(fileName);
                }
            }
            catch
            {
            }

            return false;
        }
    }
}
