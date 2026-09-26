using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace Ki.Simcity.Tools
{
    /// <summary>Reads type metadata and method IL from a managed game assembly.</summary>
    internal static class Program
    {
        /// <summary>Print matching types, or find fields by their declared type.</summary>
        private static int Main(string[] args)
        {
            if (args.Length < 2 || args.Length > 3 || !File.Exists(args[0]))
            {
                Console.Error.WriteLine("Usage: AssemblyInspector <assembly.dll> <type-fragment|@fields> [method-fragment|field-type-fragment]");
                return 2;
            }

            using (var assembly = AssemblyDefinition.ReadAssembly(args[0]))
            {
                var types = assembly.MainModule.Types.SelectMany(AllTypes).ToArray();
                var filter = args[1];
                var detail = args.Length == 3 && args[2].Length > 0 ? args[2] : null;

                if (filter == "@fields") return PrintFields(types, detail);

                var matches = types.Where(type => type.FullName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                foreach (var type in matches) PrintType(type, detail);

                if (matches.Length != 0) return 0;
                Console.Error.WriteLine("No types matched '" + filter + "'.");
                return 1;
            }
        }

        /// <summary>Find all fields whose declared type contains the supplied text.</summary>
        private static int PrintFields(IEnumerable<TypeDefinition> types, string fieldType)
        {
            if (string.IsNullOrWhiteSpace(fieldType))
            {
                Console.Error.WriteLine("@fields requires a field type fragment.");
                return 2;
            }

            foreach (var type in types)
                foreach (var field in type.Fields.Where(field => field.FieldType.FullName.IndexOf(fieldType, StringComparison.OrdinalIgnoreCase) >= 0))
                    Console.WriteLine(type.FullName + ": " + field.FieldType + " " + field.Name);
            return 0;
        }

        /// <summary>Print one type's fields, method signatures, and selected method bodies.</summary>
        private static void PrintType(TypeDefinition type, string methodFilter)
        {
            Console.WriteLine("TYPE " + type.FullName + " : " + type.BaseType);
            foreach (var field in type.Fields)
                Console.WriteLine("  FIELD " + field.Attributes + " " + field.FieldType + " " + field.Name);
            foreach (var method in type.Methods)
                Console.WriteLine("  METHOD " + method.Attributes + " " + method.ReturnType + " " + method.Name + "(" +
                                  string.Join(", ", method.Parameters.Select(parameter => parameter.ParameterType + " " + parameter.Name)) + ")");

            // IL can be long, so only print bodies when the caller names a method.
            if (methodFilter == null) return;
            foreach (var method in type.Methods.Where(method => method.HasBody && method.Name.IndexOf(methodFilter, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                Console.WriteLine("  IL " + method.Name);
                foreach (var instruction in method.Body.Instructions)
                    Console.WriteLine("    " + instruction);
            }
        }

        /// <summary>Include nested classes so compiler generated methods can be inspected.</summary>
        private static IEnumerable<TypeDefinition> AllTypes(TypeDefinition type)
        {
            yield return type;
            foreach (var nested in type.NestedTypes.SelectMany(AllTypes))
                yield return nested;
        }
    }
}
