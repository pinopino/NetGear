using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace NetGear.Rpc.Generator
{
    class Program
    {
        static string input_path;
        static string output_path;
        static string name_space_gen;
        static string name_space_ref;
        static string head_note;
        static int _index_for_prototype = 100; // 起始从100开始
        static void Main(string[] args)
        {
            var head_template = ConfigurationManager.AppSettings["head_note"].Trim();
            head_note = string.Format(head_template, Environment.NewLine, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            input_path = ConfigurationManager.AppSettings["input_path"].Trim();
            output_path = ConfigurationManager.AppSettings["output_path"].Trim();
            name_space_gen = ConfigurationManager.AppSettings["namespace_gen"].Trim();
            name_space_ref = ConfigurationManager.AppSettings["namespace_ref"].Trim();

            if (string.IsNullOrWhiteSpace(input_path))
            {
                Print("请指定输入文件路径！", 3);
                goto End;
            }

            if (string.IsNullOrWhiteSpace(output_path))
            {
                Print("请指定输出文件路径！", 3);
                goto End;
            }

            if (string.IsNullOrWhiteSpace(name_space_gen))
            {
                Print("请指定生成类的名称空间！", 3);
                goto End;
            }
            name_space_ref = name_space_ref == name_space_gen ? string.Empty : Environment.NewLine + string.Format("using {0};", name_space_ref);

            // 生成服务文件
            Print("按 'y/Y' 生成服务类文件...");
            var key = string.Empty;
            do
            {
                key = Console.ReadLine();
                if (key == "Y" || key == "y")
                {
                    Generate();

                    //DirHelper.Redirect(output_path);

                    Print("生成完毕！");
                    break;
                }
                Console.WriteLine("按‘quit’退出");
            }
            while (key != "quit");

            End:
            Print("结束！");
            Console.Read();
            Environment.Exit(0);
        }

        static bool Generate()
        {
            var is_file = Path.HasExtension(input_path);
            string[] input_file_paths = null;
            if (is_file)
            {
                input_file_paths = new string[] { input_path };
            }
            else
            {
                input_file_paths = Directory.GetFiles(input_path, "*.cs");
                if (input_file_paths.Length == 0)
                {
                    Print("指定目录下没有任何cs文件！", 3);
                    return false;
                }
            }

            EnsurePath(output_path);

            ConsoleProgressBar progress = GetProgressBar();
            try
            {
                var proto_types = new Dictionary<Type, string>();
                for (int i = 0; i < input_file_paths.Length; i++)
                {
                    InnerGenerate(input_file_paths[i], proto_types);

                    if (progress != null)
                    {
                        // 打印进度
                        ProgressPrint(progress, (i + 1), input_file_paths.Length);
                    }
                }

                // 如果有proto类型，为它们生成注册代码
                var out_str = string.Empty;
                if (proto_types.Keys.Count > 0)
                {
                    out_str = string.Format(File.ReadAllText("BaseProxy.txt"),
                        head_note,
                        name_space_ref,
                        name_space_gen,
                        Environment.NewLine + string.Join(Environment.NewLine, proto_types.Values.Select(p => p)) + ";");
                }
                else
                {
                    out_str = string.Format(File.ReadAllText("BaseProxy.txt"), 
                        head_note,
                        name_space_ref, 
                        name_space_gen, 
                        ";");
                }
                File.WriteAllText(Path.Combine(output_path, "BaseProxy.cs"), out_str);
            }
            catch (GeneratorParseException ex)
            {
                Print(ex.Message, 3);
                return false;
            }
            catch (Exception ex)
            {
                Print(ex.Message, 3);
                return false;
            }

            return true;
        }

        static Assembly DynamicComplie(string source_file)
        {
            var text = File.ReadAllText(source_file);
            var tree = SyntaxFactory.ParseSyntaxTree(text);

            var compilation = CSharpCompilation.Create("proxy.dll", new[] { tree },
                references: GetGlobalReferences(),
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assembly compiledAssembly = null;
            using (var ms = new MemoryStream())
            {
                EmitResult result = compilation.Emit(ms);
                if (!result.Success)
                {
                    IEnumerable<Diagnostic> failures = result.Diagnostics.Where(diagnostic =>
                        diagnostic.IsWarningAsError ||
                        diagnostic.Severity == DiagnosticSeverity.Error);

                    Console.WriteLine();
                    foreach (Diagnostic diagnostic in failures)
                    {
                        Print(string.Format("{0}: {1}", diagnostic.Id, diagnostic.GetMessage()), 3);
                    }
                }
                else
                {
                    ms.Seek(0, SeekOrigin.Begin);
                    compiledAssembly = Assembly.Load(ms.ToArray());
                }
            }

            return compiledAssembly;
        }

        static void InnerGenerate(string file, Dictionary<Type, string> proto_types)
        {
            var index = 0;
            // 从文件动态编译程序集
            var compiledAssembly = DynamicComplie(file);
            // 获取程序集的导出类型
            var types = compiledAssembly.ExportedTypes;
            // 所有标记了[ProtoContract]的类型全部记录下来，需要为它们生成动态注册到proto的逻辑
            foreach (var type in types.OrderBy(p => p.Name))
            {
                if (!type.IsInterface)
                {
                    if (Attribute.GetCustomAttribute(type, typeof(ProtoContractAttribute)) != null)
                    {
                        proto_types.Add(type, string.Format("\t\t\t\t.AddSubType({0}, typeof(InvokeParam<{1}>))", _index_for_prototype++, type.Name));
                    }
                }
            }

            // 检测所有接口类型，生成代理类型
            foreach (var type in types)
            {
                if (!type.IsInterface)
                {
                    continue;
                }

                var methods_str = new StringBuilder();
                var ordered_methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                                        .OrderBy(p => (p.Name + "|" + string.Join("|", p.GetParameters().Select(k => k.ParameterType.Name))))
                                        .ToArray();
                var return_type = string.Empty;
                var method_name = string.Empty;
                var parameters = string.Empty;
                for (int i = 0; i < ordered_methods.Length; i++)
                {
                    return_type = ordered_methods[i].ReturnType == typeof(void) ? "void" : GenericTypeString(ordered_methods[i].ReturnType);
                    method_name = ordered_methods[i].Name;

                    var parameters_str = new StringBuilder();
                    foreach (var item in ordered_methods[i].GetParameters())
                    {
                        if (item.ParameterType.IsByRef || item.ParameterType.IsMarshalByRef)
                        {
                            throw new GeneratorParseException(string.Format("类型{0}的方法{1}(...{2}...)存在按引用传参调用的情况，目前暂不支持", type.Name, method_name, item.ParameterType.Name));
                        }

                        var ptype = string.Empty;
                        if (item.ParameterType.IsGenericType)
                        {
                            ptype = GenericTypeString(item.ParameterType);
                        }
                        else
                        {
                            ptype = item.ParameterType.Name;
                        }
                        parameters_str.Append(string.Format("{0} {1}, ", ptype, item.Name));
                    }

                    if (i == 0)
                    {
                        methods_str.AppendLine(string.Format("public {0} {1}({2})", return_type, method_name, parameters_str.ToString().TrimEnd(", ")));
                    }
                    else
                    {
                        methods_str.AppendLine(string.Format("\t\tpublic {0} {1}({2})", return_type, method_name, parameters_str.ToString().TrimEnd(", ")));
                    }
                    methods_str.AppendLine("\t\t{");
                    var call_str = string.Join(", ", ordered_methods[i].GetParameters().Select(p => p.Name));
                    methods_str.AppendLine(string.Format("\t\t\tvar ret = _client.InvokeMethod(_serviceHash, {0}{1});",
                        ++index,
                        call_str == string.Empty ? string.Empty : ", " + call_str));
                    if (return_type != "void")
                        methods_str.AppendLine(string.Format("\t\t\treturn ({0})ret;", return_type));

                    if (i < ordered_methods.Length - 1)
                    {
                        methods_str.AppendLine("\t\t}");
                        methods_str.AppendLine();
                    }
                    else
                    {
                        methods_str.AppendLine("\t\t}");
                    }
                }

                var proxy_type = (type.Name + "Proxy").Substring(1);
                var proxy_derive = type.Name;
                var client_type = "StreamedRpcClient";
                var out_str = string.Format(File.ReadAllText("ServiceTemplate.txt"),
                    head_note,
                    name_space_ref,
                    name_space_gen,
                    proxy_type,
                    proxy_derive,
                    client_type,
                    proxy_type,
                    client_type,
                    proxy_derive,
                    proxy_derive,
                    methods_str.ToString());

                if (!Directory.Exists(output_path))
                {
                    Directory.CreateDirectory(output_path);
                }
                File.WriteAllText(Path.Combine(output_path, proxy_type + ".cs"), out_str);
            }
        }

        static IEnumerable<MetadataReference> GetGlobalReferences()
        {
            var returnList = new List<MetadataReference>();

            //The location of the .NET assemblies
            var assemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location);

            /* 
                * Adding some necessary .NET assemblies
                * These assemblies couldn't be loaded correctly via the same construction as above,
                * in specific the System.Runtime.
                */
            returnList.Add(MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "mscorlib.dll")));
            returnList.Add(MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.dll")));
            returnList.Add(MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Core.dll")));
            returnList.Add(MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Runtime.dll")));
            returnList.Add(MetadataReference.CreateFromFile(typeof(ProtoContractAttribute).Assembly.Location));
            returnList.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

            return returnList;
        }

        static string GenericTypeString(Type t)
        {
            if (!t.IsGenericType)
            {
                return t.Name;
            }
            var genericTypeName = t.GetGenericTypeDefinition().Name;
            genericTypeName = genericTypeName.Substring(0,
                genericTypeName.IndexOf('`'));
            var genericArgs = string.Join(",", t.GetGenericArguments().Select(ta => GenericTypeString(ta)).ToArray());
            return genericTypeName + "<" + genericArgs + ">";
        }

        static string GetSourceCode(Type t)
        {
            var sb = new StringBuilder();
            if (t.IsClass)
                sb.AppendFormat("public class {0}\n\t{{\n", t.Name);
            if (t.IsValueType && !t.IsEnum)
                sb.AppendFormat("public struct {0}\n\t{{\n", t.Name);
            if (t.IsEnum)
                sb.AppendFormat("public enum {0}\n\t{{\n", t.Name);

            foreach (var field in t.GetFields())
            {
                sb.AppendFormat("\t\tpublic {0} {1};\n",
                    field.FieldType.Name,
                    field.Name);
            }

            foreach (var prop in t.GetProperties())
            {
                sb.AppendFormat("\t\tpublic {0} {1} {{{2}{3}}}\n",
                    prop.PropertyType.Name,
                    prop.Name,
                    prop.CanRead ? " get;" : "",
                    prop.CanWrite ? " set; " : " ");
            }

            sb.Append("\t}");
            return sb.ToString();
        }

        static void EnsurePath(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
            Directory.CreateDirectory(path);
        }

        public static ConsoleProgressBar GetProgressBar()
        {
            return new ConsoleProgressBar(Console.CursorLeft, Console.CursorTop, 50, ProgressBarType.Character);
        }

        private static void ProgressPrint(ConsoleProgressBar progress, long index, long total)
        {
            progress.Dispaly(Convert.ToInt32((index / (total * 1.0)) * 100));
        }

        static void Print(string message, int level = 1)
        {
            switch (level)
            {
                case 1: Console.ForegroundColor = ConsoleColor.White; break;
                case 2: Console.ForegroundColor = ConsoleColor.DarkYellow; break;
                case 3: Console.ForegroundColor = ConsoleColor.DarkRed; break;
                default: Console.ForegroundColor = ConsoleColor.White; break;
            }

            Console.WriteLine();
            Console.WriteLine(message);
            Console.WriteLine();
            Console.ResetColor();
        }
    }
}
