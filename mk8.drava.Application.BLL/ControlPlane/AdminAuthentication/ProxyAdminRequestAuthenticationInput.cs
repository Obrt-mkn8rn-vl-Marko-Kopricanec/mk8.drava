using Mk8.Drava.Application.BLL.ControlPlane.AdminAudit;
using Mk8.Drava.Application.BLL.ControlPlane.RuntimeGuards;
using System.Net;

namespace Mk8.Drava.Application.BLL.ControlPlane.AdminAuthentication;
public sealed record ProxyAdminRequestAuthenticationInput(string Method, string Path, string? RemoteClientAddress, ProxyAdminPresentedCredentials PresentedCredentials);
