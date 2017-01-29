// Guids.cs
// MUST match guids.h
using System;

namespace ImageVisualizerVSPackage
{
    static class GuidList
    {
        public const string guidAutoPkgString = "e05cbc8a-0ba5-4f83-a454-7753d9c7379e";
        public const string guidAutoCmdSetString = "7999a625-1309-4c41-a8f1-0492373b86bc";
        public const string guidToolWindowPersistanceString = "b52a68d2-3a11-404e-8bb7-65afa2481f75";

        public static readonly Guid guidAutoCmdSet = new Guid(guidAutoCmdSetString);
    };
}