// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Shared/Debugger/StringValuesDictionaryDebugView.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.Extensions.Primitives;

namespace Microsoft.AspNetCore.Shared;

// This type is designed to be a debug proxy for dictionary types.
// The constructor accepts an enumerable because many of the header collection types implement IHeaderDictionary which doesn't directly implement IDictionary.
internal sealed class StringValuesDictionaryDebugView
{
    private readonly IEnumerable<KeyValuePair<string, StringValues>> _enumerable;

    public StringValuesDictionaryDebugView(IEnumerable<KeyValuePair<string, StringValues>> enumerable)
    {
        _enumerable = enumerable;
    }

    [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
    public DictionaryItemDebugView<string, string>[] Items
    {
        get
        {
            var keyValuePairs = new List<DictionaryItemDebugView<string, string>>();
            foreach (var kvp in _enumerable)
            {
                keyValuePairs.Add(new DictionaryItemDebugView<string, string>(kvp.Key, kvp.Value.ToString()));
            }
            return keyValuePairs.ToArray();
        }
    }
}
