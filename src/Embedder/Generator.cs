﻿using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Embedder.Stringifiers;

namespace Embedder
{
    public sealed class Generator : IDisposable
    {
        private const int LargeObjectHeapLimit = 85000;
        private readonly StreamWriter _writer;
        private readonly MemoryStream _buffer = new MemoryStream();
        private int _indent;

        public Generator(string path)
        {
            _writer = File.CreateText(path);
        }

        public void Generate(EmbedClass embedClass)
        {
            WriteFileHeader();
            WriteCompleteLine("using System;");
            WriteNewLine();
            WriteCompleteLine($"namespace {embedClass.Namespace}");
            WriteCompleteLine("{");
            _indent++;
            WriteCodeGenAttributes();
            WriteCompleteLine($"public static partial class {embedClass.Name}");
            WriteCompleteLine("{");

            _indent++;
            WriteProperties(embedClass);
            WriteData(embedClass.Name);

            _indent--;
            WriteCompleteLine("}");
            _indent--;
            WriteCompleteLine("}");
        }

        private void WriteProperties(EmbedClass embedClass)
        {
            int offset = 0;
            foreach (var property in embedClass.Properties)
            {
                var loader = Loader(property);
                var count = loader.LoadFile(property.File, _buffer);

                WriteCompleteLine($"public static ArraySegment<byte> {property.Name} => new ArraySegment<byte>(Data, {offset}, {count});");

                offset += count;
            }
        }

        private void WriteData(string className)
        {
            WriteNewLine();

            _buffer.Position = 0;
            int firstByte = _buffer.ReadByte();
            if (firstByte < 0) return;

            _writer.WriteLine("#region Data");
            WriteNewLine();
            WriteCompleteLine($"private static readonly byte[] Data = new byte[] {{ {firstByte}");
            _indent++;

            int count;
            byte[] chunk = new byte[32];
            while ((count = _buffer.Read(chunk, 0, 32)) > 0)
            {
                WriteCompleteLine(string.Join("", chunk.Take(count).Select(b => $",{b}")));
            }
            _indent--;
            WriteCompleteLine("};");
            WriteNewLine();

            // Very hacky code to make sure our embedded data is stored on the LOH
            if (_buffer.Length < LargeObjectHeapLimit)
            {
                WriteCompleteLine($"static {className}()");
                WriteCompleteLine("{");
                _indent++;
                WriteCompleteLine($"Array.Resize(ref Data, {LargeObjectHeapLimit});");
                _indent--;
                WriteCompleteLine("}");
                WriteNewLine();
            }
            _writer.WriteLine("#endregion");
        }

        public void Dispose()
        {
            _writer?.Dispose();
        }

        private void WriteNewLine()
        {
            _writer.WriteLine();
        }

        private void WriteCompleteLine(string text = "")
        {
            if (_indent > 0 && !string.IsNullOrEmpty(text)) _writer.Write(new string(' ', 4 * _indent));
            _writer.WriteLine(text);
        }

        private void WriteFileHeader()
        {
            WriteCompleteLine("//------------------------------------------------------------------------------");
            WriteCompleteLine("// <auto-generated>");
            WriteCompleteLine("//     This code was generated by a tool.");
            WriteCompleteLine("//     Runtime Version:4.0.30319.42000");
            WriteCompleteLine("//");
            WriteCompleteLine("//     Changes to this file may cause incorrect behavior and will be lost if");
            WriteCompleteLine("//     the code is regenerated.");
            WriteCompleteLine("// </auto-generated>");
            WriteCompleteLine("//------------------------------------------------------------------------------");
            WriteNewLine();
        }

        private void WriteCodeGenAttributes()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            WriteCompleteLine($"[global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"RendleLabs.Embedder.Tools\", \"{version}\")]");
            WriteCompleteLine("[global::System.Diagnostics.DebuggerNonUserCodeAttribute()]");
            WriteCompleteLine("[global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]");
        }

        private static IFileLoader Loader(EmbedProperty property)
        {
            return property.IsTextFile ? (IFileLoader) new TextFileLoader() : new BinaryFileLoader();
        }
    }
}