using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32.SafeHandles;

namespace CloudStack
{
    public class CertHelper
    {
        private const string RsaSha1Oid = "1.2.840.113549.1.1.5"; // szOID_RSA_SHA1RSA

        public static X509Certificate2 CreateCertificate(X500DistinguishedName subjectName, string friendlyName)
        {
            var key = Create2048RsaKey();
            var cert = CreateSelfSignedCertificate(key, subjectName);
            cert.FriendlyName = friendlyName;
            return cert;
        }

        private static CngKey Create2048RsaKey()
        {
            var keyCreationParameters = new CngKeyCreationParameters
            {
                ExportPolicy = CngExportPolicies.AllowExport,
                KeyCreationOptions = CngKeyCreationOptions.None,
                KeyUsage = CngKeyUsages.AllUsages,
                Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider
            };

            const int KeySize = 2048;
            keyCreationParameters.Parameters.Add(new CngProperty("Length", BitConverter.GetBytes(KeySize), CngPropertyOptions.None));

            return CngKey.Create(new CngAlgorithm("RSA"), null, keyCreationParameters);
        }

        private static X509Certificate2 CreateSelfSignedCertificate(CngKey key, X500DistinguishedName subjectName)
        {
            using (SafeCertContextHandle selfSignedCertHandle = CreateSelfSignedCertificate(key,
                                                                                            true,
                                                                                            subjectName.RawData,
                                                                                            X509CertificateCreationOptions.None, // NONE
                                                                                            RsaSha1Oid,
                                                                                            DateTime.UtcNow,
                                                                                            DateTime.UtcNow.AddYears(1)))
            {
                X509Certificate2 certificate = null;
                bool addedRef = false;
                RuntimeHelpers.PrepareConstrainedRegions();
                try
                {
                    selfSignedCertHandle.DangerousAddRef(ref addedRef);
                    certificate = new X509Certificate2(selfSignedCertHandle.DangerousGetHandle());
                }
                finally
                {
                    if (addedRef)
                    {
                        selfSignedCertHandle.DangerousRelease();
                    }
                }

                key.Dispose();

                return certificate;
            }
        }

        [SecurityCritical]
        private static SafeCertContextHandle CreateSelfSignedCertificate(CngKey key,
                                                                         bool takeOwnershipOfKey,
                                                                         byte[] subjectName,
                                                                         X509CertificateCreationOptions creationOptions,
                                                                         string signatureAlgorithmOid,
                                                                         DateTime startTime,
                                                                         DateTime endTime)
        {
            // Create an algorithm identifier structure for the signature algorithm
            CRYPT_ALGORITHM_IDENTIFIER nativeSignatureAlgorithm = new CRYPT_ALGORITHM_IDENTIFIER();
            nativeSignatureAlgorithm.pszObjId = signatureAlgorithmOid;
            nativeSignatureAlgorithm.Parameters = new CRYPTOAPI_BLOB();
            nativeSignatureAlgorithm.Parameters.cbData = 0;
            nativeSignatureAlgorithm.Parameters.pbData = IntPtr.Zero;

            // Convert the begin and expire dates to system time structures
            SYSTEMTIME nativeStartTime = new SYSTEMTIME(startTime);
            SYSTEMTIME nativeEndTime = new SYSTEMTIME(endTime);

            CERT_EXTENSIONS nativeExtensions = new CERT_EXTENSIONS();
            nativeExtensions.cExtension = 0;

            // Setup a CRYPT_KEY_PROV_INFO for the key
            CRYPT_KEY_PROV_INFO keyProvInfo = new CRYPT_KEY_PROV_INFO();
            keyProvInfo.pwszContainerName = key.UniqueName;
            keyProvInfo.pwszProvName = key.Provider.Provider;
            keyProvInfo.dwProvType = 0;     // NCRYPT
            keyProvInfo.dwFlags = 0;
            keyProvInfo.cProvParam = 0;
            keyProvInfo.rgProvParam = IntPtr.Zero;
            keyProvInfo.dwKeySpec = 0;

            //
            // Now that all of the needed data structures are setup, we can create the certificate
            //

            SafeCertContextHandle selfSignedCertHandle = null;
            unsafe
            {
                fixed (byte* pSubjectName = &subjectName[0])
                {
                    // Create a CRYPTOAPI_BLOB for the subject of the cert
                    CRYPTOAPI_BLOB nativeSubjectName = new CRYPTOAPI_BLOB();
                    nativeSubjectName.cbData = subjectName.Length;
                    nativeSubjectName.pbData = new IntPtr(pSubjectName);

                    // Now that we've converted all the inputs to native data structures, we can generate
                    // the self signed certificate for the input key.
                    using (SafeNCryptKeyHandle keyHandle = key.Handle)
                    {
                        selfSignedCertHandle = CertCreateSelfSignCertificate(keyHandle,
                                                                             ref nativeSubjectName,
                                                                             creationOptions,
                                                                             ref keyProvInfo,
                                                                             ref nativeSignatureAlgorithm,
                                                                             ref nativeStartTime,
                                                                             ref nativeEndTime,
                                                                             ref nativeExtensions);
                        if (selfSignedCertHandle.IsInvalid)
                        {
                            throw new CryptographicException(Marshal.GetLastWin32Error());
                        }
                    }
                }
            }

            Debug.Assert(selfSignedCertHandle != null, "selfSignedCertHandle != null");

            // Attach a key context to the certificate which will allow Windows to find the private key
            // associated with the certificate if the NCRYPT_KEY_HANDLE is ephemeral.
            // is done.
            using (SafeNCryptKeyHandle keyHandle = key.Handle)
            {
                CERT_KEY_CONTEXT keyContext = new CERT_KEY_CONTEXT();
                keyContext.cbSize = Marshal.SizeOf(typeof(CERT_KEY_CONTEXT));
                keyContext.hNCryptKey = keyHandle.DangerousGetHandle();
                keyContext.dwKeySpec = KeySpec.NCryptKey;

                bool attachedProperty = false;
                int setContextError = 0;

                // Run in a CER to ensure accurate tracking of the transfer of handle ownership
                RuntimeHelpers.PrepareConstrainedRegions();
                try { }
                finally
                {
                    CertificatePropertySetFlags flags = CertificatePropertySetFlags.None;
                    if (!takeOwnershipOfKey)
                    {
                        // If the certificate is not taking ownership of the key handle, then it should
                        // not release the handle when the context is released.
                        flags |= CertificatePropertySetFlags.NoCryptRelease;
                    }

                    attachedProperty = CertSetCertificateContextProperty(selfSignedCertHandle,
                                                                         CertificateProperty.KeyContext,
                                                                         flags,
                                                                         ref keyContext);
                    setContextError = Marshal.GetLastWin32Error();

                    // If we succesfully transferred ownership of the key to the certificate,
                    // then we need to ensure that we no longer release its handle.
                    if (attachedProperty && takeOwnershipOfKey)
                    {
                        keyHandle.SetHandleAsInvalid();
                    }
                }

                if (!attachedProperty)
                {
                    throw new CryptographicException(setContextError);
                }
            }

            return selfSignedCertHandle;
        }

        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern SafeCertContextHandle CertCreateSelfSignCertificate(SafeNCryptKeyHandle hCryptProvOrNCryptKey,
                                                                                   [In] ref CRYPTOAPI_BLOB pSubjectIssuerBlob,
                                                                                   X509CertificateCreationOptions dwFlags,
                                                                                   [In] ref CRYPT_KEY_PROV_INFO pKeyProvInfo,
                                                                                   [In] ref CRYPT_ALGORITHM_IDENTIFIER pSignatureAlgorithm,
                                                                                   [In] ref SYSTEMTIME pStartTime,
                                                                                   [In] ref SYSTEMTIME pEndTime,
                                                                                   [In] ref CERT_EXTENSIONS pExtensions);

        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CertSetCertificateContextProperty(SafeCertContextHandle pCertContext,
                                                                      CertificateProperty dwPropId,
                                                                      CertificatePropertySetFlags dwFlags,
                                                                      [In] ref CERT_KEY_CONTEXT pvData);

        [SecurityCritical]
        private sealed class SafeCertContextHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            private SafeCertContextHandle()
                : base(true)
            {
            }

            [DllImport("crypt32.dll")]
            [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
            [SuppressUnmanagedCodeSecurity]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool CertFreeCertificateContext(IntPtr pCertContext);

            protected override bool ReleaseHandle()
            {
                return CertFreeCertificateContext(handle);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CRYPT_ALGORITHM_IDENTIFIER
        {
            [MarshalAs(UnmanagedType.LPStr)]
            internal string pszObjId;

            internal CRYPTOAPI_BLOB Parameters;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CRYPTOAPI_BLOB
        {
            internal int cbData;

            internal IntPtr pbData; // BYTE*
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEMTIME
        {
            internal ushort wYear;
            internal ushort wMonth;
            internal ushort wDayOfWeek;
            internal ushort wDay;
            internal ushort wHour;
            internal ushort wMinute;
            internal ushort wSecond;
            internal ushort wMilliseconds;

            internal SYSTEMTIME(DateTime time)
            {
                wYear = (ushort)time.Year;
                wMonth = (ushort)time.Month;
                wDayOfWeek = (ushort)time.DayOfWeek;
                wDay = (ushort)time.Day;
                wHour = (ushort)time.Hour;
                wMinute = (ushort)time.Minute;
                wSecond = (ushort)time.Second;
                wMilliseconds = (ushort)time.Millisecond;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CERT_EXTENSIONS
        {
            internal int cExtension;

            internal IntPtr rgExtension;                // CERT_EXTENSION[cExtension]
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CERT_EXTENSION
        {
            [MarshalAs(UnmanagedType.LPStr)]
            internal string pszObjId;

            [MarshalAs(UnmanagedType.Bool)]
            internal bool fCritical;

            internal CRYPTOAPI_BLOB Value;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CRYPT_KEY_PROV_INFO
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            internal string pwszContainerName;

            [MarshalAs(UnmanagedType.LPWStr)]
            internal string pwszProvName;

            internal int dwProvType;

            internal int dwFlags;

            internal int cProvParam;

            internal IntPtr rgProvParam;        // PCRYPT_KEY_PROV_PARAM

            internal int dwKeySpec;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CERT_KEY_CONTEXT
        {
            internal int cbSize;
            internal IntPtr hNCryptKey;
            internal KeySpec dwKeySpec;
        }

        private enum KeySpec
        {
            NCryptKey = unchecked((int)0xffffffff)    // CERT_NCRYPT_KEY_SPEC
        }

        [Flags]
        private enum CertificatePropertySetFlags
        {
            None = 0x00000000,
            NoCryptRelease = 0x00000001,   // CERT_STORE_NO_CRYPT_RELEASE_FLAG
        }

        [Flags]
        private enum X509CertificateCreationOptions
        {
            /// <summary>
            ///     Do not set any flags when creating the certificate
            /// </summary>
            None = 0x00000000,
        }

        private enum CertificateProperty
        {
            KeyProviderInfo = 2,    // CERT_KEY_PROV_INFO_PROP_ID 
            KeyContext = 5,    // CERT_KEY_CONTEXT_PROP_ID
        }
    }
}