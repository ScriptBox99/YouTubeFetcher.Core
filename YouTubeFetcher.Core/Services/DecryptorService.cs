﻿using Microsoft.Extensions.Options;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using YouTubeFetcher.Core.Commands;
using YouTubeFetcher.Core.DTOs;
using YouTubeFetcher.Core.Exceptions;
using YouTubeFetcher.Core.Services.Interfaces;
using YouTubeFetcher.Core.Settings;

namespace YouTubeFetcher.Core.Services
{
    public class DecryptorService : IDecryptorService
    {
        private readonly DecryptorSettings _settings;
        private readonly IDictionary<string, IConverterCommand> _convertMap;

        public DecryptorService(IOptions<DecryptorSettings> options)
        {
            _settings = options.Value;
            _convertMap = new Dictionary<string, IConverterCommand> {
                { _settings.ReverseFunctionRegex, new ReverseConverterCommand() },
                { _settings.SliceFunctionRegex, new SliceConverterCommand() },
                { _settings.SwapFunctionRegex, new SwapConverterCommand() },
            };
        }

        public Location DecryptLocation(string js, Location location)
        {
            TryGetFirstMatch(js, _settings.DeciphererFunctionNameRegex, out var deciphererFunctionName);
            TryGetFirstMatch(js, string.Format(_settings.DeciphererFunctionBodyRegex, Regex.Escape(deciphererFunctionName)), out var deciphererFunctionBody, RegexOptions.Singleline);

            TryGetFirstMatch(deciphererFunctionBody, _settings.DeciphererDefinitionNameRegex, out var deciphererDefinitionName);
            if (string.IsNullOrEmpty(deciphererDefinitionName))
                throw new DecryptorServiceException("Couldn't find signature decipherer definition name.");

            TryGetFirstMatch(js, string.Format(_settings.DeciphererDefinitionBodyRegex, Regex.Escape(deciphererDefinitionName)), out var deciphererDefinitionBody, RegexOptions.Singleline);
            if (string.IsNullOrEmpty(deciphererDefinitionBody))
                throw new DecryptorServiceException("Couldn't find signature decipherer definition body.");

            location.Signature = ExecuteFunction(deciphererFunctionBody, deciphererDefinitionBody, location.Signature);
            location.Url += $"&{location.SignatureKey}={location.Signature}";
            return location;
        }

        private string ExecuteFunction(string deciphererFunctionBody, string deciphererDefinitionBody, string signature)
        {
            foreach (var functionLine in deciphererFunctionBody.Split(";"))
                signature = ExecuteLine(functionLine, deciphererDefinitionBody, signature);
            return signature;
        }

        private string ExecuteLine(string functionLine, string deciphererDefinitionBody, string signature)
        {
            if (!TryGetFirstMatch(functionLine, _settings.FunctionRegex, out var function)
                    || !TryGetConverter(deciphererDefinitionBody, function, out IConverterCommand command))
                return signature;

            TryGetFirstMatch(functionLine, _settings.ParametersRegex, out var indexVal);
            int.TryParse(indexVal, NumberStyles.AllowThousands, NumberFormatInfo.InvariantInfo, out int index);
            return command.Convert(signature, index);
        }

        private bool TryGetConverter(string deciphererDefinitionBody, string function, out IConverterCommand command)
        {
            var escapedFunction = Regex.Escape(function);
            command = null;
            foreach (var regex in _convertMap)
            {
                if (TryGetFirstMatch(deciphererDefinitionBody, string.Format(regex.Key, escapedFunction), out _))
                {
                    command = regex.Value;
                    break;
                }
            }
            return command != null;
        }

        private bool TryGetFirstMatch(string input, string pattern, out string value, RegexOptions regexOptions = RegexOptions.None)
        {
            value = string.Empty;
            var match = Regex.Match(input, pattern, regexOptions);
            if (!match.Success)
                return false;
            value = match.Groups[1].Value;
            return true;
        }
    }
}