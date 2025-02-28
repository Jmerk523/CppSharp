using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CppSharp.AST;
using CppSharp.Generators;
using CppSharp.Generators.C;
using CppSharp.Generators.CLI;
using CppSharp.Generators.Cpp;
using CppSharp.Generators.CSharp;
using CppSharp.Parser;
using CppSharp.Passes;
using CppSharp.Utils;
using CppSharp.Types;

namespace CppSharp
{
    public class Driver : IDisposable
    {
        public DriverOptions Options { get; }
        public ParserOptions ParserOptions { get; set; }
        public BindingContext Context { get; private set; }
        public Generator Generator { get; private set; }

        public bool HasCompilationErrors { get; set; }

        public Driver(DriverOptions options)
        {
            Options = options;
            ParserOptions = new ParserOptions();
        }

        Generator CreateGeneratorFromKind(GeneratorKind kind)
        {
            switch (kind)
            {
                case GeneratorKind.C:
                    return new CGenerator(Context);
                case GeneratorKind.CPlusPlus:
                    return new CppGenerator(Context);
                case GeneratorKind.CLI:
                    return new CLIGenerator(Context);
                case GeneratorKind.CSharp:
                    return new CSharpGenerator(Context);
                case GeneratorKind.QuickJS:
                    return new QuickJSGenerator(Context);
                case GeneratorKind.NAPI:
                    return new NAPIGenerator(Context);
            }

            throw new NotImplementedException();
        }

        void ValidateOptions()
        {
            if (!Options.Compilation.Platform.HasValue)
                Options.Compilation.Platform = Platform.Host;

            foreach (var module in Options.Modules)
            {
                if (string.IsNullOrWhiteSpace(module.LibraryName))
                    throw new InvalidOptionException("One of your modules has no library name.");

                if (module.OutputNamespace == null)
                    module.OutputNamespace = module.LibraryName;

                for (int i = 0; i < module.IncludeDirs.Count; i++)
                {
                    var dir = new DirectoryInfo(module.IncludeDirs[i]);
                    module.IncludeDirs[i] = dir.FullName;
                }
            }

            if (Options.NoGenIncludeDirs != null)
                foreach (var incDir in Options.NoGenIncludeDirs)
                    ParserOptions.AddIncludeDirs(incDir);
        }

        public void Setup()
        {
            ValidateOptions();
            ParserOptions.Setup();
            Context = new BindingContext(Options, ParserOptions);
            Generator = CreateGeneratorFromKind(Options.GeneratorKind);
        }

        public void SetupTypeMaps() =>
            Context.TypeMaps = new TypeMapDatabase(Context);

        public void SetupDeclMaps() =>
            Context.DeclMaps = new DeclMapDatabase(Context);

        void OnSourceFileParsed(IEnumerable<string> files, ParserResult result)
        {
            OnFileParsed(files, result);
        }

        void OnFileParsed(string file, ParserResult result)
        {
            OnFileParsed(new[] { file }, result);
        }

        void OnFileParsed(IEnumerable<string> files, ParserResult result)
        {
            switch (result.Kind)
            {
                case ParserResultKind.Success:
                    Diagnostics.Message("Parsed '{0}'", string.Join(", ", files));
                    break;
                case ParserResultKind.Error:
                    Diagnostics.Error("Error parsing '{0}'", string.Join(", ", files));
                    hasParsingErrors = true;
                    break;
                case ParserResultKind.FileNotFound:
                    Diagnostics.Error("File{0} not found: '{1}'",
                        (files.Count() > 1) ? "s" : "", string.Join(",", files));
                    hasParsingErrors = true;
                    break;
            }

            for (uint i = 0; i < result.DiagnosticsCount; ++i)
            {
                var diag = result.GetDiagnostics(i);

                if (diag.Level == ParserDiagnosticLevel.Warning &&
                    !Options.Verbose)
                    continue;

                if (diag.Level == ParserDiagnosticLevel.Note)
                    continue;

                Diagnostics.Message("{0}({1},{2}): {3}: {4}",
                    diag.FileName, diag.LineNumber, diag.ColumnNumber,
                    diag.Level.ToString().ToLower(), diag.Message);
            }
        }

        public bool ParseCode()
        {
            ClangParser.SourcesParsed += OnSourceFileParsed;

            var sourceFiles = Options.Modules.SelectMany(m => m.Headers);

            ParserOptions.BuildForSourceFile(Options.Modules);
            using (ParserResult result = ClangParser.ParseSourceFiles(
                sourceFiles, ParserOptions))
                Context.TargetInfo = result.TargetInfo;

            Context.ASTContext = ClangParser.ConvertASTContext(ParserOptions.ASTContext);

            ClangParser.SourcesParsed -= OnSourceFileParsed;

            return !hasParsingErrors;
        }

        public void SortModulesByDependencies()
        {
            var sortedModules = Options.Modules.TopologicalSort(m =>
            {
                var dependencies = (from library in Context.Symbols.Libraries
                                    where m.Libraries.Contains(library.FileName)
                                    from module in Options.Modules
                                    where library.Dependencies.Intersect(module.Libraries).Any()
                                    select module).ToList();
                if (m != Options.SystemModule)
                    m.Dependencies.Add(Options.SystemModule);
                m.Dependencies.AddRange(dependencies);
                return m.Dependencies;
            });
            Options.Modules.Clear();
            Options.Modules.AddRange(sortedModules);
        }

        public bool ParseLibraries()
        {
            ClangParser.LibraryParsed += OnFileParsed;
            foreach (var module in Options.Modules)
            {
                using (var linkerOptions = new LinkerOptions())
                {
                    foreach (var libraryDir in module.LibraryDirs)
                        linkerOptions.AddLibraryDirs(libraryDir);

                    foreach (string library in module.Libraries)
                    {
                        if (Context.Symbols.Libraries.Any(l => l.FileName == library))
                            continue;
                        linkerOptions.AddLibraries(library);
                    }

                    using (var res = ClangParser.ParseLibrary(linkerOptions))
                    {
                        if (res.Kind != ParserResultKind.Success)
                            continue;

                        for (uint i = 0; i < res.LibrariesCount; i++)
                            Context.Symbols.Libraries.Add(ClangParser.ConvertLibrary(res.GetLibraries(i)));
                    }
                }
            }
            ClangParser.LibraryParsed -= OnFileParsed;

            Context.Symbols.IndexSymbols();
            SortModulesByDependencies();

            return true;
        }

        public void SetupPasses(ILibrary library)
        {
            var TranslationUnitPasses = Context.TranslationUnitPasses;

            TranslationUnitPasses.AddPass(new ResolveIncompleteDeclsPass());
            TranslationUnitPasses.AddPass(new IgnoreSystemDeclarationsPass());

            if (Options.IsCSharpGenerator)
                TranslationUnitPasses.AddPass(new EqualiseAccessOfOverrideAndBasePass());

            TranslationUnitPasses.AddPass(new CheckIgnoredDeclsPass());

            if (Options.IsCSharpGenerator)
            {
                TranslationUnitPasses.AddPass(new TrimSpecializationsPass());
                TranslationUnitPasses.AddPass(new CheckAmbiguousFunctions());
                TranslationUnitPasses.AddPass(new GenerateSymbolsPass());
                TranslationUnitPasses.AddPass(new CheckIgnoredDeclsPass());
            }

            if (Options.IsCLIGenerator || Options.IsCSharpGenerator)
            {
                TranslationUnitPasses.AddPass(new MoveFunctionToClassPass());
                TranslationUnitPasses.AddPass(new ValidateOperatorsPass());
            }

            library.SetupPasses(this);

            TranslationUnitPasses.AddPass(new FindSymbolsPass());
            TranslationUnitPasses.AddPass(new CheckMacroPass());
            TranslationUnitPasses.AddPass(new CheckStaticClass());

            TranslationUnitPasses.AddPass(new CheckAmbiguousFunctions());
            TranslationUnitPasses.AddPass(new ConstructorToConversionOperatorPass());
            TranslationUnitPasses.AddPass(new MarshalPrimitivePointersAsRefTypePass());

            if (Options.IsCLIGenerator || Options.IsCSharpGenerator)
            {
                TranslationUnitPasses.AddPass(new CheckOperatorsOverloadsPass());
            }

            TranslationUnitPasses.AddPass(new CheckVirtualOverrideReturnCovariance());
            TranslationUnitPasses.AddPass(new CleanCommentsPass());

            Generator.SetupPasses();

            TranslationUnitPasses.AddPass(new FlattenAnonymousTypesToFields());
            TranslationUnitPasses.AddPass(new CleanInvalidDeclNamesPass());
            TranslationUnitPasses.AddPass(new FieldToPropertyPass());
            TranslationUnitPasses.AddPass(new CheckIgnoredDeclsPass());
            TranslationUnitPasses.AddPass(new CheckFlagEnumsPass());
            TranslationUnitPasses.AddPass(new MakeProtectedNestedTypesPublicPass());

            if (Options.IsCSharpGenerator)
            {
                TranslationUnitPasses.AddPass(new GenerateAbstractImplementationsPass());
                TranslationUnitPasses.AddPass(new MultipleInheritancePass());
            }

            if (Options.IsCLIGenerator || Options.IsCSharpGenerator)
            {
                TranslationUnitPasses.AddPass(new DelegatesPass());
                TranslationUnitPasses.AddPass(new GetterSetterToPropertyPass());
            }

            TranslationUnitPasses.AddPass(new StripUnusedSystemTypesPass());

            if (Options.IsCSharpGenerator)
            {
                TranslationUnitPasses.AddPass(new SpecializationMethodsWithDependentPointersPass());
                TranslationUnitPasses.AddPass(new ParamTypeToInterfacePass());
            }

            TranslationUnitPasses.AddPass(new CheckDuplicatedNamesPass());

            TranslationUnitPasses.AddPass(new MarkUsedClassInternalsPass());

            if (Options.IsCLIGenerator || Options.IsCSharpGenerator)
            {
                TranslationUnitPasses.RenameDeclsUpperCase(RenameTargets.Any & ~RenameTargets.Parameter);
                TranslationUnitPasses.AddPass(new CheckKeywordNamesPass());
            }

            Context.TranslationUnitPasses.AddPass(new HandleVariableInitializerPass());
        }

        public void ProcessCode()
        {
            Context.RunPasses();
            Generator.Process();
        }

        public List<GeneratorOutput> GenerateCode()
        {
            return Generator.Generate();
        }

        public void SaveCode(IEnumerable<GeneratorOutput> outputs)
        {
            var outputPath = Path.GetFullPath(Options.OutputDir);

            if (!Directory.Exists(outputPath))
                Directory.CreateDirectory(outputPath);

            foreach (var output in outputs.Where(o => o.TranslationUnit.IsValid))
            {
                var fileBase = output.TranslationUnit.FileNameWithoutExtension;

                if (Options.UseHeaderDirectories)
                {
                    var dir = Path.Combine(outputPath, output.TranslationUnit.FileRelativeDirectory);
                    Directory.CreateDirectory(dir);
                    fileBase = Path.Combine(output.TranslationUnit.FileRelativeDirectory, fileBase);
                }

                if (Options.GenerateName != null)
                    fileBase = Options.GenerateName(output.TranslationUnit);

                foreach (var template in output.Outputs)
                {
                    var fileRelativePath = $"{fileBase}.{template.FileExtension}";

                    var file = Path.Combine(outputPath, fileRelativePath);
                    WriteGeneratedCodeToFile(file, template.Generate());

                    if (output.TranslationUnit.Module != null)
                        output.TranslationUnit.Module.CodeFiles.Add(file);

                    Diagnostics.Message("Generated '{0}'", fileRelativePath);
                }
            }
        }

        private void WriteGeneratedCodeToFile(string file, string generatedCode)
        {
            var fi = new FileInfo(file);

            if (!fi.Exists || fi.Length != generatedCode.Length ||
                File.ReadAllText(file) != generatedCode)
                File.WriteAllText(file, generatedCode);
        }

        public bool CompileCode(Module module)
        {
            var msBuildGenerator = new MSBuildGenerator(Context, module, libraryMappings);
            msBuildGenerator.Process();
            string csproj = Path.Combine(Options.OutputDir,
                $"{module.LibraryName}.{msBuildGenerator.FileExtension}");
            File.WriteAllText(csproj, msBuildGenerator.Generate());

            string output = ProcessHelper.Run("dotnet", $"build {csproj}",
                out int error, out string errorMessage);
            if (error == 0)
            {
                Diagnostics.Message($@"Compilation succeeded: {
                    libraryMappings[module] = Path.Combine(
                        Options.OutputDir, $"{module.LibraryName}.dll")}.");
                return true;
            }

            Diagnostics.Error(output);
            Diagnostics.Error(errorMessage);
            return false;
        }

        public void AddTranslationUnitPass(TranslationUnitPass pass)
        {
            Context.TranslationUnitPasses.AddPass(pass);
        }

        public void AddGeneratorOutputPass(GeneratorOutputPass pass)
        {
            Context.GeneratorOutputPasses.AddPass(pass);
        }

        public void Dispose()
        {
            Generator.Dispose();
            Context.TargetInfo?.Dispose();
            ParserOptions.Dispose();
        }

        private bool hasParsingErrors;
        private static readonly Dictionary<Module, string> libraryMappings = new Dictionary<Module, string>();
    }

    public static class ConsoleDriver
    {
        public static void Run(ILibrary library)
        {
            var options = new DriverOptions();
            using (var driver = new Driver(options))
            {
                library.Setup(driver);

                driver.Setup();

                if (driver.Options.Verbose)
                    Diagnostics.Level = DiagnosticKind.Debug;

                if (!options.Quiet)
                    Diagnostics.Message("Parsing libraries...");

                if (!driver.ParseLibraries())
                    return;

                if (!options.Quiet)
                    Diagnostics.Message("Parsing code...");

                if (!driver.ParseCode())
                {
                    Diagnostics.Error("CppSharp has encountered an error while parsing code.");
                    return;
                }

                new CleanUnitPass { Context = driver.Context }.VisitASTContext(driver.Context.ASTContext);
                options.Modules.RemoveAll(m => m != options.SystemModule && !m.Units.GetGenerated().Any());

                if (!options.Quiet)
                    Diagnostics.Message("Processing code...");

                driver.SetupPasses(library);
                driver.SetupTypeMaps();
                driver.SetupDeclMaps();

                library.Preprocess(driver, driver.Context.ASTContext);

                driver.ProcessCode();
                library.Postprocess(driver, driver.Context.ASTContext);

                if (!options.Quiet)
                    Diagnostics.Message("Generating code...");

                if (!options.DryRun)
                {
                    var outputs = driver.GenerateCode();

                    library.GenerateCode(driver, outputs);

                    foreach (var output in outputs)
                    {
                        foreach (var pass in driver.Context.GeneratorOutputPasses.Passes)
                        {
                            pass.VisitGeneratorOutput(output);
                        }
                    }

                    driver.SaveCode(outputs);
                    if (driver.Options.IsCSharpGenerator && driver.Options.CompileCode)
                        driver.Options.Modules.Any(m => !driver.CompileCode(m));
                }
            }
        }
    }
}
