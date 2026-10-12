using System.Text.Json;
using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class PrivateHttp2SettingsTests
{
    [Fact]
    public void MissingSettingsPreserveBothExistingBootstrapDefaults()
    {
        var application = BootstrapFile.Parse<ApplicationBootstrap>("{}"u8.ToArray());
        var agent = BootstrapFile.Parse<NodeAgentBootstrap>("{}"u8.ToArray());
        Assert.Equal(new PrivateHttp2Settings(), application.PrivateHttp2);
        Assert.Equal(application.PrivateHttp2, agent.PrivateHttp2);
        Assert.Equal(65536, application.PrivateHttp2.InitialStreamWindowBytes);
        Assert.Equal(262144, application.PrivateHttp2.InitialConnectionWindowBytes);
        Assert.Equal(128, application.PrivateHttp2.MaximumStreamsPerConnection);
        application.PrivateHttp2.Validate();
    }

    [Theory]
    [InlineData(65534, 262144, 128)]
    [InlineData(16777217, 262144, 128)]
    [InlineData(65536, 65534, 128)]
    [InlineData(65536, 67108865, 128)]
    [InlineData(65536, 262144, 0)]
    [InlineData(65536, 262144, 1025)]
    public void InvalidReceiveCapacityRefusesBeforeListenerConfiguration(int streamBytes, int connectionBytes, int streams)
    {
        var settings = new PrivateHttp2Settings { InitialStreamWindowBytes = streamBytes, InitialConnectionWindowBytes = connectionBytes, MaximumStreamsPerConnection = streams };
        Assert.Throws<InvalidDataException>(settings.Validate);
    }

    [Fact]
    public void ExplicitSettingsAreStrictlyParsedWithoutInventingDeploymentFacts()
    {
        var bootstrap = BootstrapFile.Parse<ApplicationBootstrap>("{\"privateHttp2\":{\"initialStreamWindowBytes\":262144,\"initialConnectionWindowBytes\":1048576,\"maximumStreamsPerConnection\":16}}"u8.ToArray());
        bootstrap.PrivateHttp2.Validate();
        Assert.Equal(262144, bootstrap.PrivateHttp2.InitialStreamWindowBytes);
        Assert.Equal(1048576, bootstrap.PrivateHttp2.InitialConnectionWindowBytes);
        Assert.Equal(16, bootstrap.PrivateHttp2.MaximumStreamsPerConnection);
        Assert.Throws<JsonException>(() => BootstrapFile.Parse<ApplicationBootstrap>("{\"privateHttp2\":{\"unknown\":1}}"u8.ToArray()));
        Assert.Throws<JsonException>(() => BootstrapFile.Parse<ApplicationBootstrap>("{\"privateHttp2\":null}"u8.ToArray()));
    }
}
