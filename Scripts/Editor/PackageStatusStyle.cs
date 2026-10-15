using UnityEngine;

namespace Tauntastic.Packages.Editor
{
    /// <summary>Colors and flag texts used to show the status of a package in the Updates window: one color and one flag per status and per kind of failure.</summary>
    public static class PackageStatusStyle
    {
        public static readonly Color Checking = new Color(0.45f, 0.45f, 0.45f);
        public static readonly Color UpToDate = new Color(0.20f, 0.55f, 0.30f);
        public static readonly Color UpdateAvailable = new Color(0.90f, 0.55f, 0.10f);
        public static readonly Color Installable = new Color(0.10f, 0.58f, 0.65f);
        public static readonly Color SourceFolder = new Color(0.38f, 0.40f, 0.82f);
        public static readonly Color NotFoundOrPrivate = new Color(0.75f, 0.22f, 0.22f);
        public static readonly Color TokenRejected = new Color(0.60f, 0.28f, 0.70f);
        public static readonly Color Network = new Color(0.25f, 0.45f, 0.75f);
        public static readonly Color InvalidResponse = new Color(0.65f, 0.50f, 0.10f);

        /// <summary>The color of the flag and of the row tint for a package.</summary>
        public static Color GetColor(PackageUpdateInfo info)
        {
            switch (info.Status)
            {
                case PackageUpdateStatus.UpToDate: return UpToDate;
                case PackageUpdateStatus.UpdateAvailable: return UpdateAvailable;
                case PackageUpdateStatus.Installable: return Installable;
                case PackageUpdateStatus.SourceFolder: return SourceFolder;
                case PackageUpdateStatus.Failed: return GetColor(info.FailureKind);
                default: return Checking;
            }
        }

        /// <summary>The color that stands for a kind of failure.</summary>
        public static Color GetColor(PackageFailureKind kind)
        {
            switch (kind)
            {
                case PackageFailureKind.NotFoundOrPrivate: return NotFoundOrPrivate;
                case PackageFailureKind.TokenRejected: return TokenRejected;
                case PackageFailureKind.Network: return Network;
                case PackageFailureKind.InvalidResponse: return InvalidResponse;
                default: return Checking;
            }
        }

        /// <summary>The short text of the flag shown on a package.</summary>
        public static string GetFlag(PackageUpdateInfo info)
        {
            switch (info.Status)
            {
                case PackageUpdateStatus.UpToDate: return "UP TO DATE";
                case PackageUpdateStatus.UpdateAvailable: return "UPDATE AVAILABLE";
                case PackageUpdateStatus.Installable: return "INSTALLABLE";
                case PackageUpdateStatus.SourceFolder: return "SOURCE CODE";
                case PackageUpdateStatus.Failed: return GetFlag(info.FailureKind);
                default: return "CHECKING";
            }
        }

        /// <summary>The short text of the flag that stands for a kind of failure.</summary>
        public static string GetFlag(PackageFailureKind kind)
        {
            switch (kind)
            {
                case PackageFailureKind.NotFoundOrPrivate: return "NOT FOUND / PRIVATE";
                case PackageFailureKind.TokenRejected: return "TOKEN REJECTED";
                case PackageFailureKind.Network: return "NETWORK ERROR";
                case PackageFailureKind.InvalidResponse: return "INVALID RESPONSE";
                default: return "FAILED";
            }
        }
    }
}
