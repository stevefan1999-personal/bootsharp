// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Shared/ValueTaskExtensions/ValueTaskExtensions.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Internal;

internal static class ValueTaskExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task GetAsTask(this in ValueTask<FlushResult> valueTask)
    {
        // Try to avoid the allocation from AsTask
        if (valueTask.IsCompletedSuccessfully)
        {
            // Signal consumption to the IValueTaskSource
            valueTask.GetAwaiter().GetResult();
            return Task.CompletedTask;
        }
        else
        {
            return valueTask.AsTask();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask GetAsValueTask(this in ValueTask<FlushResult> valueTask)
    {
        // Try to avoid the allocation from AsTask
        if (valueTask.IsCompletedSuccessfully)
        {
            // Signal consumption to the IValueTaskSource
            valueTask.GetAwaiter().GetResult();
            return default;
        }
        else
        {
            return new ValueTask(valueTask.AsTask());
        }
    }
}
