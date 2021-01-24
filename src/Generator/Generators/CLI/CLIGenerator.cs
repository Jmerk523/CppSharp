using System.Collections.Generic;
using CppSharp.AST;
using CppSharp.Generators.C;

namespace CppSharp.Generators.CLI
{
    public enum NativeHandleManagement
    {
        /// <summary>
        /// Native handle nonexistent or ignored
        /// </summary>
        None,
        /// <summary>
        /// Native handle managed by SafeHandle base class
        /// </summary>
        SafeHandle,
        /// <summary>
        /// Native handle managed directly by generated code
        /// </summary>
        NativePointer,
    }

    /// <summary>
    /// C++/CLI generator responsible for driving the generation of
    /// source and header files.
    /// </summary>
    public class CLIGenerator : Generator
    {
        private readonly CppTypePrinter typePrinter;

        public CLIGenerator(BindingContext context) : base(context)
        {
            typePrinter = new CLITypePrinter(context);
        }

        public override List<CodeGenerator> Generate(IEnumerable<TranslationUnit> units)
        {
            var outputs = new List<CodeGenerator>();

            var header = new CLIHeaders(Context, units);
            outputs.Add(header);

            var source = new CLISources(Context, units);
            outputs.Add(source);

            return outputs;
        }

        public override bool SetupPasses() => true;

        public static NativeHandleManagement ClassNativeFieldManagement(Class @class, DriverOptions options = null)
        {
            if (!@class.IsStatic && @class.IsRefType)
            {
                if (options?.GenerateSafeHandles != true)
                {
                    return NativeHandleManagement.NativePointer;
                }
                else if (!@class.NeedsBase)
                {
                    return NativeHandleManagement.SafeHandle;
                }
                else if (!@class.HasRefBase())
                {
                    return NativeHandleManagement.NativePointer;
                }
                return NativeHandleManagement.SafeHandle;
            }
            return NativeHandleManagement.None;
        }

        public static bool ShouldGenerateClassNativeField(Class @class)
        {
            return ClassNativeFieldManagement(@class) != NativeHandleManagement.None;
        }

        protected override string TypePrinterDelegate(Type type)
        {
            return type.Visit(typePrinter).ToString();
        }
    }
}