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

    public sealed class CaPack
    {
        private CaPack(X509Certificate certificate, AsymmetricKeyParameter privateKey)
        {
            Certificate = certificate;
            PrivateKey = privateKey;
        }

        public X509Certificate Certificate { get; }

        public AsymmetricKeyParameter PrivateKey { get; }

        static CaPack Create(SX.X509Certificate2 certificate)
        {

            var cert = DotNetUtilities.FromX509Certificate(certificate);




            var key = DotNetUtilities.GetRsaKeyPair(SX.RSACertificateExtensions.GetRSAPrivateKey(certificate)).Private;

            return new CaPack(cert, key);

        }



        public static CaPack Create(byte[] rawDate)
        {
            return Create(new SX.X509Certificate2(rawDate, string.Empty, SX.X509KeyStorageFlags.Exportable));
        }
    }

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


        static X509V3CertificateGenerator GenerateTls(
            X509Name issuerName,
            AsymmetricKeyParameter issuerPublicKey,
            X509Name subjectName,
            AsymmetricKeyParameter subjectPublicKey,
            int days,
            string[] subjectNames
            )
        {
            var certGen = new X509V3CertificateGenerator();

            certGen.SetIssuerDN(issuerName);

            certGen.SetSubjectDN(subjectName);

            certGen.SetSerialNumber(GenerateSerialNumber(Random));

            SetDateTime(certGen, days);

            certGen.SetPublicKey(subjectPublicKey);

            SetBasicConstraints(certGen, false);

            SetExtendedKeyUsage(certGen);

            SetuthorityKeyIdentifier(certGen, issuerPublicKey);

            SetSubjectPublicKey(certGen, subjectPublicKey);

            SetSubjectAlternativeNames(certGen, subjectNames);


            return certGen;
        }

        static SX.X509Certificate2 GenerateTls(
            CaPack ca,
            string name,
            int keySize,
            int days,
            string[] subjectNames)
        {
            var subjectName = new X509Name($"CN={name}");

            var subjectKey = GenerateRsaKeyPair(Random, keySize);

            var certGen = GenerateTls(
                ca.Certificate.IssuerDN,
                ca.Certificate.GetPublicKey(),
                subjectName,
                subjectKey.Public,
                days,
                subjectNames);

            var x509 = certGen.Generate(new Asn1SignatureFactory(
                PkcsObjectIdentifiers.Sha256WithRsaEncryption.Id,
                ca.PrivateKey));





            return AsForm(x509, subjectKey, Random);

        }


        public static SX.X509Certificate2 GenerateTls(
            CaPack ca,
            string name,
            int keySize,
            int days,
            string subjectName,
            params string[] subjectNames)
        {
            return GenerateTls(ca, name, keySize, days, subjectNames.Append(subjectName).ToArray());
        }
    }
}