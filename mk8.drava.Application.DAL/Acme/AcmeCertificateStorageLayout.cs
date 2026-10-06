using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.DAL.Configuration.Loading;

namespace Mk8.Drava.Application.DAL.Acme;
public sealed record AcmeCertificateStorageLayout(string Root, string AccountsDirectory, string CertificatesDirectory, string PrivateKeysDirectory, string MetadataDirectory);
