using Mk8.Drava.Application.BLL.ControlPlane.Forwarding;
using System.Collections.ObjectModel;

namespace Mk8.Drava.Application.BLL.ControlPlane.Resilience;
public sealed record ProxyRetryPlan(ProxyRetryAdmissionDecision Admission, bool IsAllowed, int MaxAttempts);
