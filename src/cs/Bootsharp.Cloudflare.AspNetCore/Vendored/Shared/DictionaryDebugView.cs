// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Shared/Debugger/DictionaryDebugView.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;

namespace Microsoft.AspNetCore.Shared;

internal sealed class DictionaryDebugView<TKey, TValue> where TKey : notnull
{
    private readonly IDictionary<TKey, TValue> _dict;

    public DictionaryDebugView(IDictionary<TKey, TValue> dictionary)
    {
        _dict = dictionary;
    }

    [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
    public DictionaryItemDebugView<TKey, TValue>[] Items
    {
        get
        {
            var keyValuePairs = new KeyValuePair<TKey, TValue>[_dict.Count];
            _dict.CopyTo(keyValuePairs, 0);
            var items = new DictionaryItemDebugView<TKey, TValue>[keyValuePairs.Length];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = new DictionaryItemDebugView<TKey, TValue>(keyValuePairs[i]);
            }
            return items;
        }
    }
}
