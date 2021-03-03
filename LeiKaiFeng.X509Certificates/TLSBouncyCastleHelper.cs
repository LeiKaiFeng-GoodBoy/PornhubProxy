using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Crypto.Tls;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.X509;
using Org.BouncyCastle.X509.Extension;
using System;
using System.Linq;
using System.IO;
using SX = System.Security.Cryptography.X509Certificates;

namespace LeiKaiFeng.X509Certificates
{
    public static class TLSBouncyCastleHelper
    {
        static readonly SecureRandom Random = new SecureRandom();

        static BigInteger GenerateSerialNumber(SecureRandom random)
        {
            return BigIntegers.CreateRandomInRange(
                    BigInteger.One, BigInteger.ValueOf(Int64.MaxValue), random);
        }


        static AsymmetricCipherKeyPair GenerateRsaKeyPair(SecureRandom random, int keySize)
        {
            var key = new RsaKeyPairGenerator();

            key.Init(new KeyGenerationParameters(random, keySize));

            return key.GenerateKeyPair();
        }

        static byte[] AsByteArray(X509Certificate certificate, AsymmetricCipherKeyPair key,
            string password, SecureRandom random)
        {

            string friendlyName = certificate.SubjectDN.ToString();

            var certificateEntry = new X509CertificateEntry(certificate);

            var store = new Pkcs12Store();

            store.SetCertificateEntry(friendlyName, certificateEntry);

            store.SetKeyEntry(friendlyName, new AsymmetricKeyEntry(key.Private), new[] { certificateEntry });

            var stream = new MemoryStream();

            store.Save(stream, password.ToCharArray(), random);
            
            stream.Position = 0;
            
            return stream.ToArray();
        }

        static SX.X509Certificate2 AsForm(X509Certificate certificate,
            AsymmetricCipherKeyPair key, SecureRandom random)
        {
            const string S = "54646454";

            var buffer = AsByteArray(certificate, key, S, random);


            return new SX.X509Certificate2(buffer, S, SX.X509KeyStorageFlags.Exportable);
        }


        static void SetDateTime(X509V3CertificateGenerator generator, int days)
        {
            generator.SetNotBefore(DateTime.UtcNow);
            generator.SetNotAfter(DateTime.UtcNow.AddDays(days));
        }

        static void SetBasicConstraints(X509V3CertificateGenerator generator, bool ca)
        {
            generator.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(ca));
        }

        static void SetSubjectAlternativeNames(X509V3CertificateGenerator generator, string[] names)
        {
            var subjectAlternativeNames =
                names.Select((s) => new GeneralName(GeneralName.DnsName, s)).ToArray();

            var subjectAlternativeNamesExtension = new DerSequence(subjectAlternativeNames);

            generator.AddExtension(
                X509Extensions.SubjectAlternativeName, false, subjectAlternativeNamesExtension);

        }

        static void SetExtendedKeyUsage(X509V3CertificateGenerator generator)
        {
            var usages = new[] { KeyPurposeID.IdKPServerAuth };
            generator.AddExtension(
                X509Extensions.ExtendedKeyUsage,
                false,
                new ExtendedKeyUsage(usages));
        }

        static void SetuthorityKeyIdentifier(X509V3CertificateGenerator generator, AsymmetricKeyParameter issuerPublic)
        {
            //Authority Key Identifier
            var authorityKeyIdentifier = new AuthorityKeyIdentifierStructure(issuerPublic);
            generator.AddExtension(
                X509Extensions.AuthorityKeyIdentifier, false, authorityKeyIdentifier);


        }

        static void SetSubjectPublicKey(X509V3CertificateGenerator generator, AsymmetricKeyParameter subjectPublic)
        {
            //Subject Key Identifier
            var subjectKeyIdentifier = new SubjectKeyIdentifier(
                SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(subjectPublic));

            generator.AddExtension(
                X509Extensions.SubjectKeyIdentifier, false, subjectKeyIdentifier);

        }

        static void AsFrom(SX.X509Certificate2 certificate, out X509Certificate one, out AsymmetricKeyParameter tow)
        {

            one = DotNetUtilities.FromX509Certificate(certificate);




            tow = DotNetUtilities.GetRsaKeyPair(SX.RSACertificateExtensions.GetRSAPrivateKey(certificate)).Private;

        }



        static void AsFrom(byte[] rawDate, out X509Certificate one, out AsymmetricKeyParameter tow)
        {
            AsFrom(new SX.X509Certificate2(rawDate, string.Empty, SX.X509KeyStorageFlags.Exportable), out one, out tow);
        }

        public static SX.X509Certificate2 GenerateCA(
            string name,
            int keySize,
            int days)
        {
            var key = GenerateRsaKeyPair(Random, keySize);

            var cert = new X509V3CertificateGenerator();

            var subject = new X509Name($"CN={name}");

            cert.SetIssuerDN(subject);

            cert.SetSubjectDN(subject);

            cert.SetSerialNumber(GenerateSerialNumber(Random));

            SetDateTime(cert, days);

            cert.SetPublicKey(key.Public);

            SetBasicConstraints(cert, true);

            var x509 = cert.Generate(new Asn1SignatureFactory(PkcsObjectIdentifiers.Sha256WithRsaEncryption.Id, key.Private));

            return AsForm(x509, key, Random);

        }

        public static SX.X509Certificate2 GenerateTls(
            byte[] caRawDate,
            string name,
            int keySize,
            int days,
            string[] subjectNames)
        {
            var subject = new X509Name($"CN={name}");



            AsFrom(caRawDate, out var ca_certificate, out var ca_private_key);



            var key = GenerateRsaKeyPair(Random, keySize);

            var cert = new X509V3CertificateGenerator();

            cert.SetIssuerDN(ca_certificate.SubjectDN);
            
            cert.SetSubjectDN(subject);
            
            cert.SetSerialNumber(GenerateSerialNumber(Random));
            
            SetDateTime(cert, days);
            
            cert.SetPublicKey(key.Public);

            SetBasicConstraints(cert, false);
            
            SetExtendedKeyUsage(cert);

            SetuthorityKeyIdentifier(cert, ca_certificate.GetPublicKey());

            SetSubjectPublicKey(cert, key.Public);

            SetSubjectAlternativeNames(cert, subjectNames);


            var x509 = cert.Generate(new Asn1SignatureFactory(PkcsObjectIdentifiers.Sha256WithRsaEncryption.Id, ca_private_key));





            return AsForm(x509, key, Random);

        }


        static void Main()
        {
            var ca = TLSBouncyCastleHelper.GenerateTls(File.ReadAllBytes("myCA.pfx"), "x", 2048, 2, new string[] { "cn.com" });

            File.WriteAllBytes("t.cer", ca.Export(SX.X509ContentType.Cert));
        }
    }
}