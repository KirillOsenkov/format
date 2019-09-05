﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.CodingConventions;

namespace Microsoft.CodeAnalysis.Tools.Formatters
{
    internal sealed class CharsetFormatter : DocumentFormatter
    {
        public override FormatType FormatType => FormatType.Whitespace;
        protected override string FormatWarningDescription => Resources.Fix_file_encoding;

        private static Encoding Utf8 => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private static Encoding Latin1 => Encoding.GetEncoding("iso-8859-1");

        internal override Task<SourceText> FormatFileAsync(
            Document document,
            SourceText sourceText,
            OptionSet options,
            ICodingConventionsSnapshot codingConventions,
            FormatOptions formatOptions,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                if (!TryGetCharset(codingConventions, out var encoding)
                    || sourceText.Encoding.Equals(encoding))
                {
                    return sourceText;
                }

                return SourceText.From(sourceText.ToString(), encoding, sourceText.ChecksumAlgorithm);
            });
        }

        private static bool TryGetCharset(ICodingConventionsSnapshot codingConventions, out Encoding encoding)
        {
            if (codingConventions.TryGetConventionValue("charset", out string charsetOption))
            {
                encoding = GetCharset(charsetOption);
                return true;
            }

            encoding = null;
            return false;
        }

        public static Encoding GetCharset(string charsetOption)
        {
            switch (charsetOption)
            {
                case "latin1":
                    return Latin1;
                case "utf-8-bom":
                    return Encoding.UTF8; // UTF-8 with BOM Marker
                case "utf-16be":
                    return Encoding.BigEndianUnicode; // Big Endian with BOM Marker
                case "utf-16le":
                    return Encoding.Unicode; // Little Endian with BOM Marker
                case "utf-8":
                default:
                    return Utf8;
            }
        }
    }
}
