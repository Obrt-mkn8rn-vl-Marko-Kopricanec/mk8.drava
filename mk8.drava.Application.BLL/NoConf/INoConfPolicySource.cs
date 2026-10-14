namespace Mk8.Drava.Application.BLL.NoConf;

public interface INoConfPolicySource
{
    // Null means no configured input; an explicit default policy is a separate value.
    ValueTask<NoConfPolicy?> ReadAsync(CancellationToken cancellationToken);
}
