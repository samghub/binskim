﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Microsoft.CodeAnalysis.Sarif;

namespace Microsoft.CodeAnalysis.IL.Rules
{
    internal class VulnerableDependencyDescriptor : PropertiesDictionary
    {
        private static PerLanguageOption<StringSetCollection> s_fileHashes { get; } =
            new PerLanguageOption<StringSetCollection>(
                nameof(VulnerableDependencyDescriptor), nameof(FileHashes), defaultValue: () => { return new StringSetCollection(); });

        private static PerLanguageOption<string> s_id { get; } =
            new PerLanguageOption<string>(
                nameof(VulnerableDependencyDescriptor), nameof(Id), defaultValue: () => { return String.Empty; });

        private static PerLanguageOption<string> s_dependencyName { get; } =
            new PerLanguageOption<string>(
                nameof(VulnerableDependencyDescriptor), nameof(DependencyName), defaultValue: () => { return String.Empty; });

        private static PerLanguageOption<string> s_vulnerabilityDescription { get; } =
            new PerLanguageOption<string>(
                nameof(VulnerableDependencyDescriptor), nameof(VulnerabilityDescription), defaultValue: () => { return String.Empty; });

        private static PerLanguageOption<string> s_resolution { get; } =
            new PerLanguageOption<string>(
                nameof(VulnerableDependencyDescriptor), nameof(Resolution), defaultValue: () => { return String.Empty; });

        public StringSetCollection FileHashes
        {
            get { return GetProperty(s_fileHashes); }
        }

        public string Id
        {
            get { return GetProperty(s_id); }
            set { this.SetProperty(s_id, value); }
        }
        public string DependencyName
        {
            get { return GetProperty(s_dependencyName); }
            set { this.SetProperty(s_dependencyName, value); }
        }

        public string VulnerabilityDescription
        {
            get { return GetProperty(s_vulnerabilityDescription); }
            set { this.SetProperty(s_vulnerabilityDescription, value); }
        }

        public string Resolution
        {
            get { return GetProperty(s_resolution); }
            set { this.SetProperty(s_resolution, value); }
        }
    }
}
