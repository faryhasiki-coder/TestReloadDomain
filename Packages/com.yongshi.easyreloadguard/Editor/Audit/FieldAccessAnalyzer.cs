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
    /// 内容修改按“读取字段后紧接着对同类型调用修改方法”判断，属于启发式：经局部变量转手或参数中夹杂同类型调用时可能漏判，
    /// 只用于报告中的建议文字。证明“内容不变”时改用从严的只读白名单（<see cref="FieldUsage.UnsafeUses"/>）：每次读取字段后，
    /// 按求值栈找出真正消费这个引用的指令，不在白名单中的用法（修改、传给其他方法、存入局部变量、跨分支等）一律记为可能修改。
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

        /// <summary>证明内容不变时允许的实例调用：只读取，不修改集合，也不返回可写的内部引用。</summary>
        static readonly HashSet<string> k_ReadOnlyCalls = new HashSet<string>(StringComparer.Ordinal)
        {
            "get_Count", "get_Length", "get_Item", "get_Keys", "get_Values", "Contains", "ContainsKey", "ContainsValue",
            "TryGetValue", "GetEnumerator", "IndexOf",
        };

        // 方法名相同不代表只读：自定义集合和接口实现不能凭 Count / TryGetValue 之类的名字获得证明。
        static readonly HashSet<Type> k_CollectionTypes = new HashSet<Type>
        {
            typeof(List<>), typeof(Dictionary<,>), typeof(HashSet<>), typeof(SortedDictionary<,>), typeof(SortedList<,>),
            typeof(LinkedList<>), typeof(Queue<>), typeof(Stack<>),
        };

        /// <summary>向前查找消费者的最大指令数，超过视为无法确认。</summary>
        const int k_MaxLookahead = 64;

        const BindingFlags k_AllDeclared =
            BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        public static TypeAccess Analyze(Type type)
        {
            if (type.IsGenericType && !type.IsGenericTypeDefinition) type = type.GetGenericTypeDefinition();
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
            if (method is ConstructorInfo) return method.IsStatic ? "静态初始化" : "实例构造";
            var name = method.Name;
            if (name.StartsWith("<", StringComparison.Ordinal))
            {
                var source = Inner(name);
                var kind = name.Contains(">g__") ? "局部函数" : "lambda";
                return source == ".cctor" ? $"静态初始化（{kind}）" : $"{source}（{kind}）";
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
            var il = ReadIL(method);
            if (il == null)
            {
                if (!method.IsAbstract) access.Error = $"无法读取 {DisplayName(method)} 的方法体";
                return;
            }
            if (il.Length == 0) return;

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
            var collectionAllocation = -1;
            var previous = -1; // 上一条与上上一条指令的起始位置，用于判断初值是否为默认值
            var beforePrevious = -1;
            var branchSeen = false;

            for (var pos = 0; pos < il.Length;)
            {
                var start = pos;
                if (!TryDecode(il, ref pos, out var op, out var operandStart))
                    throw new InvalidOperationException($"{name} 包含无法解析的 IL 指令");
                if (op.FlowControl == FlowControl.Branch || op.FlowControl == FlowControl.Cond_Branch) branchSeen = true;
                var last = previous;
                var lastButOne = beforePrevious;
                beforePrevious = previous;
                previous = start;

                if (isInitializer && (op == OpCodes.Newobj || op == OpCodes.Newarr)) collectionAllocation = start;

                if (op == OpCodes.Stsfld || op == OpCodes.Ldsflda || op == OpCodes.Ldsfld)
                {
                    var field = Resolve(() => module.ResolveField(BitConverter.ToInt32(il, operandStart), typeArgs, methodArgs));
                    if (field == null) throw new InvalidOperationException($"{name} 中的字段引用无法解析");
                    if (!field.IsStatic) continue;
                    if (op == OpCodes.Ldsfld)
                    {
                        pending = field;
                        var read = access.UsageOf(field, true);
                        if (read != null) CheckUse(il, pos, module, typeArgs, methodArgs, field, read, facts, isInitializer, name);
                        continue;
                    }

                    if (op == OpCodes.Ldsflda && pos < il.Length && il[pos] == OpCodes.Ldfld.Value) continue; // 取地址后读成员，视为读取

                    var usage = access.UsageOf(field, true);
                    if (usage == null) continue;
                    facts.Touched.Add(TypeAccess.Key(field));
                    if (isInitializer)
                    {
                        usage.InitializerWrites = true;
                        if (branchSeen || !WritesDefault(il, op, pos, last, lastButOne)) usage.InitializerNonDefault = true;
                        if (op == OpCodes.Stsfld && initializerFill && IsCollection(field.FieldType)) usage.InitializerMutates = true;
                        if (op == OpCodes.Stsfld && (IsCollection(field.FieldType) || field.FieldType.IsArray) &&
                            (branchSeen || !IsDirectCollectionInitialValue(il, last, collectionAllocation, start, module, typeArgs, methodArgs)))
                            usage.InitializerContentsUnknown = true;
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
                    collectionAllocation = -1;
                    pending = null;
                    continue;
                }

                if (op == OpCodes.Call || op == OpCodes.Callvirt)
                {
                    var callee = Resolve(() => module.ResolveMethod(BitConverter.ToInt32(il, operandStart), typeArgs, methodArgs));
                    if (callee == null) throw new InvalidOperationException($"{name} 中的方法引用无法解析");
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

        static bool IsDirectCollectionInitialValue(byte[] il, int last, int allocation, int store,
            Module module, Type[] typeArgs, Type[] methodArgs)
        {
            if (last < 0 || !TryDecode(il, ref last, out var op, out var operand)) return false;
            if (op == OpCodes.Ldnull) return true;
            if (allocation < 0 || !TryDecode(il, ref allocation, out op, out operand)) return false;
            if (op == OpCodes.Newobj)
            {
                var ctor = Resolve(() => module.ResolveMethod(BitConverter.ToInt32(il, operand), typeArgs, methodArgs));
                if (ctor == null || !IsKnownCollection(ctor.DeclaringType) || ctor.GetParameters().Length != 0) return false;
            }
            else if (op != OpCodes.Newarr) return false;

            // 只认从新建对象直接流到字段的引用。集合初始化器会 dup 后调用 Add；局部别名、传参或其他字段存储均不能作证明。
            // 不能仅凭前面出现过 Add 就放行：它可能修改的是另一个集合，而当前字段来自外部共享实例。
            var stack = new List<bool> { true };
            for (var pos = allocation; pos < store;)
            {
                if (!TryDecode(il, ref pos, out op, out operand)) return false;
                if (op.FlowControl == FlowControl.Branch || op.FlowControl == FlowControl.Cond_Branch ||
                    op.FlowControl == FlowControl.Return || op.FlowControl == FlowControl.Throw || op == OpCodes.Calli) return false;
                if (op == OpCodes.Dup)
                {
                    if (stack.Count == 0) return false;
                    stack.Add(stack[stack.Count - 1]);
                    continue;
                }

                MethodBase callee = null;
                if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj)
                {
                    callee = Resolve(() => module.ResolveMethod(BitConverter.ToInt32(il, operand), typeArgs, methodArgs));
                    if (callee == null) return false;
                }
                var pops = Pops(op, callee);
                if (pops > stack.Count) return false;
                var first = stack.Count - pops;
                for (var i = first; i < stack.Count; i++)
                {
                    if (!stack[i]) continue;
                    var fillsCollection = callee != null && !callee.IsStatic && op != OpCodes.Newobj &&
                                          IsKnownCollection(callee.DeclaringType) && k_CollectionMutators.Contains(callee.Name);
                    if (i != first || (!fillsCollection && !IsArrayStore(op))) return false;
                }
                stack.RemoveRange(first, pops);
                for (var i = 0; i < Pushes(op, callee); i++) stack.Add(false);
            }
            return stack.Count == 1 && stack[0];
        }

        static bool IsKnownCollection(Type type) =>
            type != null && type.IsGenericType && k_CollectionTypes.Contains(type.GetGenericTypeDefinition());

        static void RecordMutation(TypeAccess access, MethodFacts facts, FieldInfo field, bool isInitializer, string name)
        {
            var usage = access.UsageOf(field, true);
            if (usage == null) return;
            facts.Touched.Add(TypeAccess.Key(field));
            if (isInitializer) usage.InitializerMutates = true;
            else usage.AddMutator(name);
        }

        /// <summary>
        /// 类型初始化中的这次写入是否写的是默认值：stsfld 前一条是 ldnull 或值为 0 的常量（允许中间一条 conv），
        /// 或 ldsflda 之后紧跟 initobj。无法确认时按非默认值处理。
        /// </summary>
        static bool WritesDefault(byte[] il, OpCode op, int next, int last, int lastButOne)
        {
            if (op == OpCodes.Ldsflda)
            {
                if (next >= il.Length) return false;
                var at = next;
                return TryDecode(il, ref at, out var following, out _) && following == OpCodes.Initobj;
            }

            if (op != OpCodes.Stsfld || last < 0) return false;
            var position = last;
            if (!TryDecode(il, ref position, out var pushed, out var operand)) return false;
            if (pushed.Name.StartsWith("conv.", StringComparison.Ordinal))
            {
                if (lastButOne < 0) return false;
                position = lastButOne;
                if (!TryDecode(il, ref position, out pushed, out operand)) return false;
            }

            return IsZeroConstant(il, pushed, operand, position);
        }

        static bool IsZeroConstant(byte[] il, OpCode op, int operand, int end)
        {
            if (op == OpCodes.Ldnull || op == OpCodes.Ldc_I4_0) return true;
            if (op != OpCodes.Ldc_I4_S && op != OpCodes.Ldc_I4 && op != OpCodes.Ldc_I8 && op != OpCodes.Ldc_R4 && op != OpCodes.Ldc_R8) return false;
            for (var i = operand; i < end; i++)
                if (il[i] != 0)
                    return false;
            return true;
        }

        /// <summary>
        /// 读取字段（ldsfld）之后，按求值栈找出消费这个引用的指令，判断是否属于只读白名单；不属于时记为可能修改（UnsafeUses）。
        /// 消费者是对字段的实例调用时，同时记下调用的方法名，用于判断退出方法是否取消或释放了字段。
        /// 值类型 ldsfld 得到的是副本，不分析。类型初始化中允许集合的修改方法（初始填充）。
        /// </summary>
        static void CheckUse(byte[] il, int pos, Module module, Type[] typeArgs, Type[] methodArgs, FieldInfo field, FieldUsage usage,
            MethodFacts facts, bool isInitializer, string name)
        {
            var fieldType = field.FieldType;
            if (fieldType.IsValueType) return;
            var depth = 0; // 字段引用之上压入的栈项数
            for (var steps = 0; pos < il.Length && steps < k_MaxLookahead; steps++)
            {
                if (!TryDecode(il, ref pos, out var op, out var operandStart)) break;
                MethodBase callee = null;
                if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj)
                {
                    callee = Resolve(() => module.ResolveMethod(BitConverter.ToInt32(il, operandStart), typeArgs, methodArgs));
                    if (callee == null) break;
                }
                else if (op == OpCodes.Calli)
                {
                    break;
                }

                var pops = Pops(op, callee);
                if (pops > depth)
                {
                    var calledOnField = !(callee is null) && !callee.IsStatic && op != OpCodes.Newobj && pops - 1 - depth == 0;
                    if (calledOnField) facts.CallsOn.Add((TypeAccess.Key(field), callee.Name));
                    if (!IsReadOnlyUse(op, callee, fieldType, pops - 1 - depth, isInitializer)) usage.AddUnsafeUse(name);
                    return;
                }

                // 字段引用仍在栈上时出现跳转或返回：之后的消费者无法逐条确认
                if (op.FlowControl == FlowControl.Branch || op.FlowControl == FlowControl.Cond_Branch ||
                    op.FlowControl == FlowControl.Return || op.FlowControl == FlowControl.Throw)
                    break;
                depth = depth - pops + Pushes(op, callee);
            }

            usage.AddUnsafeUse(name);
        }

        /// <param name="argument">字段引用在被弹出的栈项中的位置：0 为最深的一项（实例调用的 this 或第一个参数）。</param>
        static bool IsReadOnlyUse(OpCode op, MethodBase callee, Type fieldType, int argument, bool isInitializer)
        {
            if (op == OpCodes.Pop || op == OpCodes.Brtrue || op == OpCodes.Brtrue_S || op == OpCodes.Brfalse || op == OpCodes.Brfalse_S) return true;
            if (op == OpCodes.Ceq || op == OpCodes.Cgt_Un || op == OpCodes.Clt_Un) return true;
            if (op == OpCodes.Ldlen) return argument == 0;
            if (op.Name.StartsWith("ldelem", StringComparison.Ordinal) && op != OpCodes.Ldelema) return argument == 0;
            if (callee == null || op == OpCodes.Newobj) return false;

            if (!callee.IsStatic && argument == 0)
            {
                if (!IsCallOn(callee, fieldType) || !IsKnownCollection(callee.DeclaringType)) return false;
                return k_ReadOnlyCalls.Contains(callee.Name) ||
                       (isInitializer && k_CollectionMutators.Contains(callee.Name));
            }

            return false; // 传给其他方法（包括返回延迟枚举的 LINQ 调用）不属于只读证明。
        }

        static int Pops(OpCode op, MethodBase callee)
        {
            if (op.StackBehaviourPop == StackBehaviour.Varpop)
            {
                if (callee == null) return 0; // ret：由跳转判断处理
                var count = callee.GetParameters().Length;
                if (op != OpCodes.Newobj && !callee.IsStatic) count++;
                return count;
            }

            return Count(op.StackBehaviourPop.ToString(), nameof(StackBehaviour.Pop0));
        }

        static int Pushes(OpCode op, MethodBase callee)
        {
            if (op.StackBehaviourPush == StackBehaviour.Varpush)
            {
                if (op == OpCodes.Newobj) return 1;
                return callee is MethodInfo method && method.ReturnType != typeof(void) ? 1 : 0;
            }

            return Count(op.StackBehaviourPush.ToString(), nameof(StackBehaviour.Push0));
        }

        /// <summary>StackBehaviour 的名称按“_”分段，每段一项，如 Popref_popi_pop1 为 3 项；Pop0、Push0 为 0。</summary>
        static int Count(string behaviour, string none) => behaviour == none ? 0 : behaviour.Split('_').Length;

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

        /// <summary>解码 pos 处的一条指令，pos 前进到下一条；遇到未知字节返回 false（无法确定操作数长度）。</summary>
        internal static bool TryDecode(byte[] il, ref int pos, out OpCode op, out int operandStart)
        {
            EnsureOpCodes();
            op = default;
            operandStart = pos;
            var code = il[pos++];
            if (code == 0xFE)
            {
                if (pos >= il.Length) return false;
                op = s_TwoByte[il[pos++]];
            }
            else
            {
                op = s_OneByte[code];
            }

            if (op.Size == 0) return false;
            operandStart = pos;
            pos += OperandSize(op, il, pos);
            return pos <= il.Length;
        }

        /// <summary>读取方法体 IL；无方法体（extern、抽象）或运行时拒绝读取时返回 null。</summary>
        internal static byte[] ReadIL(MethodBase method)
        {
            try
            {
                return method.GetMethodBody()?.GetILAsByteArray();
            }
            catch (Exception)
            {
                return null;
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

        /// <summary>集合初值不是可直接确认的独占构造，不能排除非空内容或外部别名。</summary>
        public bool InitializerContentsUnknown { get; set; }

        /// <summary>类型初始化中写入了非默认值（无法确认时也算）。只有初值为默认值的字段才能用 ResetMode.Default。</summary>
        public bool InitializerNonDefault { get; set; }

        /// <summary>
        /// 读取字段后，用法不在只读白名单中的方法（修改内容、传给其他方法、存入局部变量、跨分支等）。
        /// 为空时，字段引用的内容在声明类型内只被读取。
        /// </summary>
        public List<string> UnsafeUses { get; } = new List<string>();

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

        public void AddUnsafeUse(string name)
        {
            if (!UnsafeUses.Contains(name)) UnsafeUses.Add(name);
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

        /// <summary>以字段为实例的调用：(字段, 方法名)。</summary>
        public readonly HashSet<((Module, int), string)> CallsOn = new HashSet<((Module, int), string)>();
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
                    if (facts.IsInitializer)
                    {
                        usage.InitializerWrites = true;
                        usage.InitializerNonDefault = true; // 经访问器写入的值无法确认是否为默认值
                    }
                    else
                    {
                        usage.AddWriter(facts.Name);
                    }
                }
            }
        }

        /// <summary>从 root 出发、沿类型内调用可达的方法中赋值或修改过的字段（不含 lambda 等延迟执行的代码）。</summary>
        public HashSet<(Module, int)> TouchedFrom(MethodBase root)
        {
            var touched = new HashSet<(Module, int)>();
            foreach (var facts in Reachable(root)) touched.UnionWith(facts.Touched);
            return touched;
        }

        /// <summary>从 root 出发、沿类型内调用可达的方法中，以字段为实例的调用：(字段, 方法名)。</summary>
        public HashSet<((Module, int), string)> CallsFrom(MethodBase root)
        {
            var calls = new HashSet<((Module, int), string)>();
            foreach (var facts in Reachable(root)) calls.UnionWith(facts.CallsOn);
            return calls;
        }

        IEnumerable<MethodFacts> Reachable(MethodBase root)
        {
            var visited = new HashSet<(Module, int)>();
            var stack = new Stack<(Module, int)>();
            stack.Push(Key(root));
            while (stack.Count > 0)
            {
                var key = stack.Pop();
                if (!visited.Add(key) || !m_Methods.TryGetValue(key, out var facts)) continue;
                yield return facts;
                foreach (var callee in facts.Callees) stack.Push(callee);
            }
        }
    }
}
