// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Shared/RoslynUtils/CodeWriter.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Generator-side only: this assembly is an analyzer, so nothing here reaches the wasm.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CodeDom.Compiler;
using System.Globalization;
using System.IO;

internal sealed class CodeWriter : IndentedTextWriter
{
    public CodeWriter(StringWriter stringWriter, int baseIndent) : base(stringWriter)
    {
        Indent = baseIndent;
    }

    public void StartBlock()
    {
        this.WriteLine("{");
        this.Indent++;
    }

    public void EndBlock()
    {
        this.Indent--;
        this.WriteLine("}");
    }

    public void EndBlockWithComma()
    {
        this.Indent--;
        this.WriteLine("},");
    }

    public void EndBlockWithSemicolon()
    {
        this.Indent--;
        this.WriteLine("};");
    }

    // The IndentedTextWriter adds the indentation
    // _after_ writing the first line of text. This
    // method can be used ot initialize indentation
    // when an emit method might only emit one line
    // of code or when the code writer is emitting
    // indented code as part of a larger string.
    public void InitializeIndent()
    {
        for (var i = 0; i < Indent; i++)
        {
            Write(DefaultTabString);
        }
    }
}
