using System;
using System.Collections.ObjectModel;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Linq;

namespace LeiKaiFeng.X509Certificates
{

    public static class TLSCertificate
    {

        static X509Extension CreateSubAltName(string[] subjectAltNames)
        {
            var builder = new SubjectAlternativeNameBuilder();

            Array.ForEach(subjectAltNames, (s) => builder.AddDnsName(s));

            return builder.Build(false);
        }

        static void AddExtension(Collection<X509Extension> extensions, string[] subjectAltNames)
        {


            extensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DataEncipherment, false));
            extensions.Add(new X509BasicConstraintsExtension(false, true, 0, false));
            extensions.Add(CreateSubAltName(subjectAltNames));
        }

        static byte[] Create20SerialNumber()
        {
            byte[] buffer = new byte[20];

            RandomNumberGenerator.Create().GetNonZeroBytes(buffer);

            return buffer;
        }

        public static X509Certificate2 CreateTlsCertificate(X509Certificate2 caCertificate, string commonName, int keySize, int days, params string[] subjectAltNames)
        {
            string subjectName = $"CN = {commonName}";

            var rsa = RSA.Create(keySize);

            var certificateRequest = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            AddExtension(certificateRequest.CertificateExtensions, subjectAltNames);



            var dateTime = DateTime.UtcNow;

            X509Certificate2 tlsCertificate = certificateRequest.Create(caCertificate, new DateTimeOffset(dateTime), new DateTimeOffset(dateTime.AddDays(days)), Create20SerialNumber());

            return new X509Certificate2(tlsCertificate.CopyWithPrivateKey(rsa).Export(X509ContentType.Pfx), string.Empty, X509KeyStorageFlags.Exportable);
        }


        public static X509Certificate2 CreateCA(string commonName, int keySize, int days)
        {
            string subjectName = $"CN = {commonName}";

            var rsa = RSA.Create(keySize);



            var certificateRequest = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            certificateRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 1, true));

            var dateTime = DateTime.UtcNow;

            return certificateRequest.CreateSelfSigned(new DateTimeOffset(dateTime), new DateTimeOffset(dateTime.AddDays(days)));

        }

    }

}
