using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Http1;
using System.Globalization;
using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Headers;
public sealed record ForwardedHeadersListener(string Scheme, int Port);
