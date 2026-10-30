using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace EasyReloadGuard.Editor
{
    /// <summary>
    /// 只读 IL 分析：统计静态字段在声明类型（含嵌套类型、lambda 与 async/迭代器状态机）内的赋值与内容修改位置。
    /// 只读取方法体字节，不执行代码、不触发静态构造、不改写程序集。类型外的访问不分析，非私有字段的结论只覆盖声明类型内。
    /// 内容修改按“读取字段后紧接着对同类型调用修改方法”判断，属于启发式：经局部变量转手或参数中夹杂同类型调用时可能漏判。
    /// </summary>
    internal static class FieldAccessAnalyzer
    {
        static readonly Dictionary<Type, TypeAccess> s_Cache = new Dictionary<Type, TypeAccess>();
        static OpCode[] s_OneByte;
        static OpCode[] s_TwoByte;

        static readonly HashSet<string> k_CollectionMutators = new HashSet<string>(StringComparer.Ordinal)
        {
            "Add", "AddRange", "AddFirst", "AddLast", "Insert", "InsertRange", "Remove", "RemoveAt", "RemoveAll",
            "RemoveRange", "RemoveFirst", "RemoveLast", "RemoveWhere", "Clear", "TryAdd", "TryRemove", "TryUpdate",
            "AddOrUpdate", "GetOrAdd", "Push", "Pop", "TryPop", "Enqueue", "Dequeue", "TryDequeue", "set_Item", "Sort",
            "Reverse", "UnionWith", "IntersectWith", "ExceptWith", "SymmetricExceptWith", "TrimExcess",
        };

        static readonly HashSet<string> k_ArrayMutators = new HashSet<string>(StringComparer.Ordinal)
        {
            "Clear", "Fill", "Sort", "Reverse",
        };

        const BindingFlags k_AllDeclared =
            BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        public static TypeAccess Analyze(Type type)
        {
            if (!s_Cache.TryGetValue(type, out var access))
            {
                access = new TypeAccess(type);
                try
                {
                    foreach (var method in MethodsOf(type)) ScanMethod(type, method, access);
                    access.AttributeAccessorWrites();
                }
                catch (Exception e)
                {
                    access.Error = e.GetBaseException().Message;
                }

                s_Cache[type] = access;
            }

            return access;
        }

        /// <summary>泛型闭合构造上的字段换算为泛型定义上的字段（IL 分析与声明复核都针对定义）。</summary>
        public static FieldInfo OpenField(FieldInfo field)
        {
            var type = field.DeclaringType;
            if (type == null || !type.IsGenericType || type.IsGenericTypeDefinition) return field;
            return type.GetGenericTypeDefinition().GetField(field.Name, ReflectionUtil.StaticMembers) ?? field;
        }

        /// <summary>面向人的方法名：编译器生成的 lambda、局部函数与状态机还原为所在的源方法。</summary>
        internal static string DisplayName(MethodBase method)
        {
            // MethodBase.IsConstructor 对静态构造返回 false，用类型判断
            if (method is ConstructorInfo) return method.IsStatic ? "类型初始化" : "实例构造";
            var name = method.Name;
            if (name.StartsWith("<", StringComparison.Ordinal))
            {
                var source = Inner(name);
                var kind = name.Contains(">g__") ? "局部函数" : "lambda";
                return source == ".cctor" ? $"类型初始化（{kind}）" : $"{source}（{kind}）";
            }

            var declaring = method.DeclaringType;
            if (declaring != null && declaring.Name.StartsWith("<", StringComparison.Ordinal) && !declaring.Name.StartsWith("<>", StringComparison.Ordinal))
                return $"{Inner(declaring.Name)}（async/迭代器）";
            if (method.IsSpecialName)
            {
                if (name.StartsWith("get_", StringComparison.Ordinal)) return $"{name.Substring(4)} 的 get 访问器";
                if (name.StartsWith("set_", StringComparison.Ordinal)) return $"{name.Substring(4)} 的 set 访问器";
            }

            return name;
        }

        static string Inner(string compilerName)
        {
            var end = compilerName.IndexOf('>');
            return end > 1 ? compilerName.Substring(1, end - 1).TrimStart('<') : compilerName;
        }

        static IEnumerable<MethodBase> MethodsOf(Type type)
        {
            foreach (var ctor in type.GetConstructors(k_AllDeclared)) yield return ctor;
            foreach (var method in type.GetMethods(k_AllDeclared)) yield return method;
            foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            foreach (var method in MethodsOf(nested))
                yield return method;
        }

        static void ScanMethod(Type owner, MethodBase method, TypeAccess access)
        {
            byte[] il;
            try
            {
                il = method.GetMethodBody()?.GetILAsByteArray();
            }
            catch (Exception)
            {
                return; // 无方法体（extern、抽象）或运行时拒绝读取
            }

            if (il == null || il.Length == 0) return;

            var module = method.Module;
            var typeArgs = method.DeclaringType != null && method.DeclaringType.IsGenericType ? method.DeclaringType.GetGenericArguments() : null;
            var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;
            var isInitializer = method is ConstructorInfo && method.IsStatic && method.DeclaringType == owner;
            var facts = access.FactsOf(method);
            var name = DisplayName(method);
            facts.Name = name;
            facts.IsInitializer = isInitializer;
            var accessor = IsAccessor(method);

            FieldInfo pending = null; // 最近一次读取、尚未被同类型调用消费的字段
            var initializerFill = false; // 类型初始化中，new 出的集合在赋给字段前已被填充（集合初始化器）

            EnsureOpCodes();
            for (var pos = 0; pos < il.Length;)
            {
                var code = il[pos++];
                OpCode op;
                if (code == 0xFE)
                {
                    if (pos >= il.Length) break;
                    op = s_TwoByte[il[pos++]];
                }
                else
                {
                    op = s_OneByte[code];
                }

                if (op.Size == 0) break; // 未知字节：无法确定操作数长度，停止分析该方法
                var operandStart = pos;
                pos += OperandSize(op, il, pos);

                if (op == OpCodes.Stsfld || op == OpCodes.Ldsflda || op == OpCodes.Ldsfld)
                {
                    var field = Resolve(() => module.ResolveField(BitConverter.ToInt32(il, operandStart), typeArgs, methodArgs));
                    if (field == null || !field.IsStatic) continue;
                    if (op == OpCodes.Ldsfld)
                    {
                        pending = field;
                        continue;
                    }

                    if (op == OpCodes.Ldsflda && pos < il.Length && il[pos] == OpCodes.Ldfld.Value) continue; // 取地址后读成员，视为读取

                    var usage = access.UsageOf(field, true);
                    if (usage == null) continue;
                    facts.Touched.Add(TypeAccess.Key(field));
                    if (isInitializer)
                    {
                        usage.InitializerWrites = true;
                        if (op == OpCodes.Stsfld && initializerFill && IsCollection(field.FieldType)) usage.InitializerMutates = true;
                    }
                    else if (accessor)
                    {
                        // 属性 setter、事件 add/remove 写入：真正的写入方是访问器的调用方，扫描完成后再归属
                        usage.Accessors.Add(TypeAccess.Key(method));
                        if (!method.IsPrivate) usage.Complete = false;
                    }
                    else
                    {
                        usage.AddWriter(name);
                    }

                    initializerFill = false;
                    pending = null;
                    continue;
                }

                if (op == OpCodes.Call || op == OpCodes.Callvirt)
                {
                    var callee = Resolve(() => module.ResolveMethod(BitConverter.ToInt32(il, operandStart), typeArgs, methodArgs));
                    if (callee == null) continue;
                    if (IsWithin(callee.DeclaringType, owner)) facts.Callees.Add(TypeAccess.Key(callee));

                    if (pending != null && IsCallOn(callee, pending.FieldType))
                    {
                        if (IsMutation(callee, pending.FieldType)) RecordMutation(access, facts, pending, isInitializer, name);
                        pending = null;
                    }
                    else if (isInitializer && !callee.IsStatic && k_CollectionMutators.Contains(callee.Name))
                    {
                        initializerFill = true;
                    }

                    continue;
                }

                if (pending != null && pending.FieldType.IsArray && IsArrayStore(op))
                {
                    RecordMutation(access, facts, pending, isInitializer, name);
                    pending = null;
                }
            }
        }

        static void RecordMutation(TypeAccess access, MethodFacts facts, FieldInfo field, bool isInitializer, string name)
        {
            var usage = access.UsageOf(field, true);
            if (usage == null) return;
            facts.Touched.Add(TypeAccess.Key(field));
            if (isInitializer) usage.InitializerMutates = true;
            else usage.AddMutator(name);
        }

        static bool IsCallOn(MethodBase callee, Type fieldType)
        {
            var declaring = callee.DeclaringType;
            if (declaring == null) return false;
            if (callee.IsStatic) return fieldType.IsArray && declaring == typeof(Array);
            try
            {
                return declaring.IsAssignableFrom(fieldType) ||
                       (declaring.IsGenericType && fieldType.IsGenericType &&
                        declaring.GetGenericTypeDefinition() == fieldType.GetGenericTypeDefinition());
            }
            catch (Exception)
            {
                return false;
            }
        }

        static bool IsAccessor(MethodBase method) =>
            method.IsSpecialName && (method.Name.StartsWith("set_", StringComparison.Ordinal) ||
                                     method.Name.StartsWith("add_", StringComparison.Ordinal) ||
                                     method.Name.StartsWith("remove_", StringComparison.Ordinal));

        static bool IsMutation(MethodBase callee, Type fieldType)
        {
            // 委托不可变（Invoke 不改变字段）；值类型经 ldsfld 得到的是副本，修改走 ldsflda，已按赋值统计
            if (fieldType.IsValueType || typeof(Delegate).IsAssignableFrom(fieldType)) return false;
            if (fieldType.IsArray) return k_ArrayMutators.Contains(callee.Name);
            if (IsCollection(fieldType)) return k_CollectionMutators.Contains(callee.Name);
            // 一般对象（缓存、对象池、注册表）：除取值器与 object 基础方法外的实例调用都可能修改其状态
            var name = callee.Name;
            return !name.StartsWith("get_", StringComparison.Ordinal) &&
                   name != "ToString" && name != "GetHashCode" && name != "Equals" && name != "GetType";
        }

        internal static bool IsCollection(Type type) => !type.IsArray && type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);

        static bool IsArrayStore(OpCode op) =>
            op == OpCodes.Stelem || op == OpCodes.Stelem_Ref || op == OpCodes.Stelem_I || op == OpCodes.Stelem_I1 ||
            op == OpCodes.Stelem_I2 || op == OpCodes.Stelem_I4 || op == OpCodes.Stelem_I8 || op == OpCodes.Stelem_R4 ||
            op == OpCodes.Stelem_R8;

        static bool IsWithin(Type type, Type owner)
        {
            for (var current = type; current != null; current = current.DeclaringType)
                if (current == owner)
                    return true;
            return false;
        }

        static T Resolve<T>(Func<T> resolve) where T : class
        {
            try
            {
                return resolve();
            }
            catch (Exception)
            {
                return null; // 引用了未加载的程序集或无法解析的泛型上下文
            }
        }

        static int OperandSize(OpCode op, byte[] il, int pos)
        {
            switch (op.OperandType)
            {
                case OperandType.InlineNone:
                    return 0;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    return 1;
                case OperandType.InlineVar:
                    return 2;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    return 8;
                case OperandType.InlineSwitch:
                    return pos + 4 <= il.Length ? 4 + 4 * BitConverter.ToInt32(il, pos) : il.Length - pos;
                default:
                    return 4;
            }
        }

        static void EnsureOpCodes()
        {
            if (s_OneByte != null) return;
            var one = new OpCode[0x100];
            var two = new OpCode[0x100];
            foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!(field.GetValue(null) is OpCode op)) continue;
                var value = (ushort)op.Value;
                if (value < 0x100) one[value] = op;
                else if ((value & 0xFF00) == 0xFE00) two[value & 0xFF] = op;
            }

            s_OneByte = one;
            s_TwoByte = two;
        }
    }

    /// <summary>某个静态字段在声明类型内的访问情况。</summary>
    internal sealed class FieldUsage
    {
        const int k_MaxNames = 5;

        public FieldUsage(bool complete)
        {
            Complete = complete;
        }

        /// <summary>
        /// 私有字段只能在声明类型内访问，结论完整；非私有字段，或经非私有的属性 setter/事件 add、remove 写入的字段，
        /// 可能在类型外被写入。
        /// </summary>
        public bool Complete { get; set; }

        /// <summary>写入该字段的属性 setter 与事件访问器（方法键），其调用方才是实际写入方。</summary>
        internal readonly HashSet<(Module, int)> Accessors = new HashSet<(Module, int)>();

        /// <summary>类型初始化（字段初值或静态构造）中赋值。</summary>
        public bool InitializerWrites { get; set; }

        /// <summary>类型初始化中填充了内容（集合初始化器或静态构造中的 Add 等）。</summary>
        public bool InitializerMutates { get; set; }

        /// <summary>类型初始化以外的赋值位置。</summary>
        public List<string> Writers { get; } = new List<string>();

        /// <summary>类型初始化以外修改内容（集合元素、数组元素、对象状态）的位置。</summary>
        public List<string> Mutators { get; } = new List<string>();

        public void AddWriter(string name)
        {
            if (!Writers.Contains(name)) Writers.Add(name);
        }

        public void AddMutator(string name)
        {
            if (!Mutators.Contains(name)) Mutators.Add(name);
        }

        public static string Names(List<string> names) =>
            names.Count <= k_MaxNames
                ? string.Join("、", names)
                : string.Join("、", names.GetRange(0, k_MaxNames)) + $" 等 {names.Count} 处";
    }

    internal sealed class MethodFacts
    {
        public string Name;
        public bool IsInitializer;
        public readonly HashSet<(Module, int)> Touched = new HashSet<(Module, int)>();
        public readonly HashSet<(Module, int)> Callees = new HashSet<(Module, int)>();
    }

    internal sealed class TypeAccess
    {
        readonly Type m_Type;
        readonly Dictionary<(Module, int), FieldUsage> m_Fields = new Dictionary<(Module, int), FieldUsage>();
        readonly Dictionary<(Module, int), MethodFacts> m_Methods = new Dictionary<(Module, int), MethodFacts>();

        public TypeAccess(Type type)
        {
            m_Type = type;
        }

        /// <summary>分析失败的原因；为 null 表示成功。</summary>
        public string Error { get; set; }

        /// <summary>泛型闭合构造上的成员与定义上的成员元数据标记相同，用 (模块, 标记) 识别同一成员。</summary>
        public static (Module, int) Key(MemberInfo member) => (member.Module, member.MetadataToken);

        /// <summary>字段在声明类型内的访问情况；分析失败或不属于该类型时返回 null。</summary>
        public FieldUsage Of(FieldInfo field) => Error == null ? UsageOf(FieldAccessAnalyzer.OpenField(field), false) : null;

        internal FieldUsage UsageOf(FieldInfo field, bool create)
        {
            var declaring = field.DeclaringType;
            if (declaring == null) return null;
            if (declaring.IsGenericType && !declaring.IsGenericTypeDefinition) declaring = declaring.GetGenericTypeDefinition();
            if (declaring != m_Type) return null;

            var key = Key(field);
            if (!m_Fields.TryGetValue(key, out var usage))
            {
                usage = new FieldUsage(field.IsPrivate);
                if (create) m_Fields[key] = usage;
            }

            return usage;
        }

        internal MethodFacts FactsOf(MethodBase method)
        {
            var key = Key(method);
            if (!m_Methods.TryGetValue(key, out var facts)) m_Methods[key] = facts = new MethodFacts();
            return facts;
        }

        /// <summary>把经访问器的写入归属到类型内调用该访问器的方法（类型初始化中调用记为初值）。</summary>
        internal void AttributeAccessorWrites()
        {
            foreach (var usage in m_Fields.Values)
            {
                if (usage.Accessors.Count == 0) continue;
                foreach (var facts in m_Methods.Values)
                {
                    if (!facts.Callees.Overlaps(usage.Accessors)) continue;
                    if (facts.IsInitializer) usage.InitializerWrites = true;
                    else usage.AddWriter(facts.Name);
                }
            }
        }

        /// <summary>从 root 出发、沿类型内调用可达的方法中赋值或修改过的字段（不含 lambda 等延迟执行的代码）。</summary>
        public HashSet<(Module, int)> TouchedFrom(MethodBase root)
        {
            var touched = new HashSet<(Module, int)>();
            var visited = new HashSet<(Module, int)>();
            var stack = new Stack<(Module, int)>();
            stack.Push(Key(root));
            while (stack.Count > 0)
            {
                var key = stack.Pop();
                if (!visited.Add(key) || !m_Methods.TryGetValue(key, out var facts)) continue;
                touched.UnionWith(facts.Touched);
                foreach (var callee in facts.Callees) stack.Push(callee);
            }

            return touched;
        }
    }
}
