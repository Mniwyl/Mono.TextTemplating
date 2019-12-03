using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using Mono.TextTemplating.CodeCompilation;
using CodeCompiler = Mono.TextTemplating.CodeCompilation.CodeCompiler;

namespace Mono.TextTemplating.Roslyn
{
	class RoslynCodeCompiler : CodeCompiler
	{
		readonly RuntimeInfo _runtime;

		public RoslynCodeCompiler (RuntimeInfo runtime)
		{
			_runtime = runtime;
		}

		public  override async Task<CodeCompilerResult> CompileFile (
			CodeCompilerArguments arguments,
			TextWriter log,
			CancellationToken token)
		{
			var references = new List<MetadataReference> ();
			foreach (var assemblyReference in arguments.AssemblyReferences) {
				var argumentsAssemblyReference = assemblyReference;
				var path = AssemblyResolver.Resolve(_runtime, argumentsAssemblyReference);
				references.Add (MetadataReference.CreateFromFile (path));
			}

			references.Add (MetadataReference.CreateFromFile (typeof(object).Assembly.Location));
			references.Add (MetadataReference.CreateFromFile (typeof(Enumerable).Assembly.Location));
			references.Add (MetadataReference.CreateFromFile (typeof(string).Assembly.Location));
			references.Add (MetadataReference.CreateFromFile (typeof(Console).Assembly.Location));
			references.Add (MetadataReference.CreateFromFile (typeof(IntPtr).Assembly.Location));
			references.Add (MetadataReference.CreateFromFile (typeof(AssemblyTargetedPatchBandAttribute).Assembly.Location));
			references.Add (MetadataReference.CreateFromFile (Assembly.Load ("netstandard, Version=2.0.0.0").Location));

			var source = File.ReadAllText (arguments.SourceFiles.Single ());
			var syntaxTree = CSharpSyntaxTree.ParseText (source);

			var compilation = CSharpCompilation.Create (
				"GeneratedTextTransformation",
				new List<SyntaxTree> {syntaxTree},
				references,
				new CSharpCompilationOptions (OutputKind.DynamicallyLinkedLibrary)
			);

			var pdbFilePath = Path.ChangeExtension(arguments.OutputPath, "pdb");

			EmitResult result;
			using (var fs = File.OpenWrite (arguments.OutputPath)) {
				using (var symbolsStream = File.OpenWrite(pdbFilePath)) {
					var emitOptions = new EmitOptions(
						debugInformationFormat: DebugInformationFormat.PortablePdb,
						pdbFilePath: pdbFilePath);


					var embeddedTexts = new List<EmbeddedText> {
						EmbeddedText.FromSource(
							arguments.SourceFiles.Single(),
							SourceText.From(source, Encoding.UTF8)),
					};

					result = compilation.Emit(
						fs,
						symbolsStream,
						embeddedTexts: embeddedTexts,
						options: emitOptions);
				}
			}

			if (result.Success) {
				return new CodeCompilerResult {
					Output = new List<string> (),
					Success = true,
					Errors = new List<CodeCompilerError> ()
				};
			}

			var failures = result.Diagnostics.Where (x => x.IsWarningAsError || x.Severity == DiagnosticSeverity.Error);

			return new CodeCompilerResult {
				Success = false,
				Output = new List<string> (),
				Errors = failures.Select (
					x => new CodeCompilerError {
						Message = x.GetMessage(),
						Column = x.Location.GetMappedLineSpan().StartLinePosition.Character,
						Line = x.Location.GetMappedLineSpan().StartLinePosition.Line,
						EndLine = x.Location.GetMappedLineSpan().EndLinePosition.Line,
						EndColumn = x.Location.GetMappedLineSpan().EndLinePosition.Character,
						IsError = x.IsWarningAsError,
						Origin = x.Location.GetMappedLineSpan().Path
					}).ToList (),
			};
		}
	}
}