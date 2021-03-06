﻿using System;
using System.Collections.Generic;
using System.Linq;
using static TsActivexGen.TSParameterType;
using System.Diagnostics;
using static TsActivexGen.Functions;
using JsDoc = System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>;
using static System.Linq.Enumerable;
using MoreLinq;

namespace TsActivexGen {
    public abstract class EqualityBase<T> : IEquatable<T> where T : class {
        public abstract bool Equals(T other);
        public override bool Equals(object other) {
            if (ReferenceEquals(null, other)) { return false; }
            if (ReferenceEquals(this, other)) { return true; }
            if (GetType() != other.GetType()) { return false; }
            return Equals(other as T);
        }
        public abstract override int GetHashCode();

        protected static bool OperatorEquals(T x, T y) {
            if (ReferenceEquals(x, null)) { return ReferenceEquals(y, null); }
            return x.Equals(y);
        }
    }

    public class TSEnumDescription {
        public Dictionary<string, TSEnumValueDescription> Members { get; } = new Dictionary<string, TSEnumValueDescription>(); //values -> string representation of value
        public JsDoc JsDoc { get; } = new JsDoc();
    }

    public class TSEnumValueDescription {
        /// <summary>String representation of value</summry>
        public string Value { get; set; } //TODO should this be an object?
        public JsDoc JsDoc { get; } = new JsDoc();
    }

    public class TSParameterDescription : EqualityBase<TSParameterDescription> {
        public ITSType Type { get; set; }
        public TSParameterType ParameterType { get; set; } = Standard;

        public override bool Equals(TSParameterDescription other) {
            return Type.Equals(other.Type)
                && ParameterType == other.ParameterType;
        }
        public override int GetHashCode() {
            unchecked {
                int hash = 17;
                hash = hash * 486187739 + Type.GetHashCode();
                hash = hash * 486187739 + ParameterType.GetHashCode();
                return hash;
            }
        }
        public override bool Equals(object other) => base.Equals(other);

        public static bool operator ==(TSParameterDescription x, TSParameterDescription y) => OperatorEquals(x, y);
        public static bool operator !=(TSParameterDescription x, TSParameterDescription y) => !OperatorEquals(x, y);

        public void MergeTypeFrom(TSParameterDescription source, string Operand) {
            if (Type is TSComposedType x) {
                x.AddPart(source.Type);
            } else if (Type != source.Type) {
                var final = new TSComposedType(Operand);
                final.AddPart(Type);
                final.AddPart(source.Type);
                if (final.Parts.Count > 1) {
                    Type = final;
                }
            }
        }
    }

    public class TSMemberDescription : EqualityBase<TSMemberDescription>, IClonable<TSMemberDescription> {
        public List<KeyValuePair<string, TSParameterDescription>> Parameters { get; set; } //(null means a property, empty means empty parameter list); this mut be a list, becaus parameter order is important
        public ITSType ReturnType { get; set; }
        public bool? ReadOnly { get; set; }
        public JsDoc JsDoc { get; } = new JsDoc();
        public List<TSPlaceholder> GenericParameters { get; } = new List<TSPlaceholder>();
        public bool Private { get; set; }

        public bool IsProperty => Parameters == null;

        public void AddParameter(string name, ITSType type) {
            if (Parameters == null) { Parameters = new List<KeyValuePair<string, TSParameterDescription>>(); }
            Parameters.Add(name, new TSParameterDescription() { Type = type });
        }
        public void AddParameter(string name, TSSimpleType type) => AddParameter(name, (ITSType)type);
        public void SetParameter(string name, ITSType type) {
            var indexOf = Parameters.IndexOf(KVP => KVP.Key == name);
            Parameters[indexOf] = KVP(name, new TSParameterDescription() { Type = type });
        }

        public override bool Equals(TSMemberDescription other) {
            if (ReadOnly != other.ReadOnly) { return false; }
            if (Parameters?.Count != other.Parameters?.Count) { return false; }
            if (!ReturnType.Equals(other.ReturnType)) { return false; }
            if (Parameters == null) { return true; }
            return Parameters.SequenceEqual(other.Parameters);
        }

        public override int GetHashCode() {
            unchecked {
                int hash = 17;
                hash = hash * 486187739 + ReturnType.GetHashCode();
                if (Parameters != null) {
                    foreach (var prm in Parameters) {
                        hash = hash * 486187739 + prm.GetHashCode();
                    }
                }
                if (ReadOnly.HasValue) {
                    hash = hash * 486187739 + ReadOnly.GetHashCode();
                }
                return hash;
            }
        }

        public IEnumerable<TSSimpleType> TypeParts() => Parameters.DefaultIfNull().Values().SelectMany(x => x.Type.TypeParts()).Concat(ReturnType.TypeParts());

        public bool TryFoldInto(TSMemberDescription destination) {
            if (!ReturnType.Equals(destination.ReturnType)) { return false; }
            if (Parameters == null ^ destination.Parameters == null) { return false; } //one is a property, the other is not

            if (Parameters == null) { return true; }

            var corresponding = Parameters.Zip(destination.Parameters).ToList();
            var equalTypes = corresponding.All((sourceP, destP) => sourceP.Value.Type.Equals(destP.Value.Type));

            if (equalTypes) { //if there is only one additional parameter, we can fold using optional (or a rest) parameter
                var allPairs = Parameters.ZipLongest(destination.Parameters, (sourceP, destP) => (sourceP: sourceP, destP: destP)).ToList();
                switch (allPairs.Count - corresponding.Count) {
                    case 0:
                        return true;
                    case var x when x > 1:
                        return false;
                    case 1:
                        var extra = allPairs.Last();
                        var (sourceName, sourceDescr) = extra.sourceP;
                        var (destName, destDescr) = extra.destP;
                        if (sourceName.IsNullOrEmpty()) { //extra parameter in destination
                            if (destDescr.ParameterType == Standard) { destDescr.ParameterType = Optional; }
                        } else { //extra parameter in source
                            if (sourceDescr.ParameterType == Standard) { sourceDescr.ParameterType = Optional; }
                            destination.Parameters.Add(sourceName, sourceDescr);
                        }
                        return true;
                }
            }

            // TODO it might be possible to fold a method with one differing type, and a rest parameter.
            if (Parameters.Values().Any(x => x.ParameterType == Rest) || destination.Parameters.Values().Any(x => x.ParameterType == Rest)) { return false; }

            /*if (Parameters.Count != destination.Parameters.Count) {
                return false;
            } else if (Parameters.Values().SequenceEqual(destination.Parameters.Values())) {
                return true;
            }*/

            var foldableMethod = true;
            var unfoldableParameters = Parameters.Values().Zip(destination.Parameters.Values(), (sourceParam, destParam) => {
                if (!foldableMethod) { return null; }
                if (sourceParam.ParameterType != destParam.ParameterType) { //if "optionality" of corresponding parameters doesn't match
                    foldableMethod = false;
                    return null;
                }
                var alreadyIncludesType = false;
                if (destParam.Type is TSComposedType x && sourceParam.Type is TSComposedType y) {
                    alreadyIncludesType = y.Parts.Except(x.Parts).None();
                } else if (destParam.Type is TSComposedType x1) {
                    alreadyIncludesType = x1.Parts.Contains(sourceParam.Type);
                } else {
                    alreadyIncludesType = destParam.Type.Equals(sourceParam.Type);
                }
                return new { sourceParam, destParam, alreadyIncludesType };
            }).Where(x => !x.alreadyIncludesType).ToList();
            if (!foldableMethod) { return false; }

            switch (unfoldableParameters.Count) {
                case 0:
                    return true;
                case var x2 when x2 > 1:
                    return false;
                default: // 1
                    var details = unfoldableParameters[0];
                    details.destParam.MergeTypeFrom(details.sourceParam, "|");
                    return true;
            }
        }

        public TSMemberDescription Clone() {
            var ret = new TSMemberDescription();
            if (Parameters != null) {
                ret.Parameters = Parameters.Select(x => x.Clone()).ToList();
            }
            ret.ReturnType = ReturnType.Clone();
            ret.ReadOnly = ReadOnly;
            ret.Private = Private;
            GenericParameters.AddRangeTo(ret.GenericParameters);
            return ret;
        }
    }

    public class TSInterfaceDescription : EqualityBase<TSInterfaceDescription> {
        public List<KeyValuePair<string, TSMemberDescription>> Members { get; } = new List<KeyValuePair<string, TSMemberDescription>>();
        public List<TSMemberDescription> Constructors { get; } = new List<TSMemberDescription>();
        public JsDoc JsDoc { get; } = new JsDoc();
        public HashSet<string> Extends { get; } = new HashSet<string>();
        public bool IsClass { get; set; }
        public List<TSPlaceholder> GenericParameters { get; } = new List<TSPlaceholder>();

        public void ConsolidateMembers() {
            //consolidate members
            var membersLookup = Members.Select((kvp, index) => (name: kvp.Key, description: kvp.Value, index)).ToLookup(x => (x.name, x.description.ReturnType));
            var positionsToRemove = new List<int>();
            foreach (var grp in membersLookup) {
                if (grp.Count() == 1) { continue; }
                ConsolidateGroup(grp.Select(x => (x.description, x.index))).AddRangeTo(positionsToRemove);
            }
            Members.RemoveMultipleAt(positionsToRemove);

            //consolidate constructors
            var constructorsLookup = Constructors.Select((ctor, index) => (ctor, index)).ToLookup(x => x.ctor.Parameters?.Keys().Joined());
            positionsToRemove = new List<int>();
            foreach (var grp in constructorsLookup) {
                if (grp.Count() == 1) { continue; }
                ConsolidateGroup(grp).AddRangeTo(positionsToRemove);
            }
            Constructors.RemoveMultipleAt(positionsToRemove);


            List<int> ConsolidateGroup(IEnumerable<(TSMemberDescription member, int index)> grp) {
                var ret = new List<int>();
                var lst = grp.Reverse().ToList();
                bool modified;
                do {
                    modified = false;
                    for (int i = lst.Count - 1; i >= 0; i -= 1) {
                        var source = lst[i];
                        for (int j = 0; j < i; j++) {
                            var dest = lst[j];
                            if (source.member.TryFoldInto(dest.member)) {
                                modified = true;
                                ret.Add(lst[i].index);
                                lst.RemoveAt(i);
                                break;
                            }
                        }
                    }
                } while (modified);
                return ret;
            }
        }

        public IEnumerable<(string interfaceName, string memberName, TSMemberDescription descr)> InheritedMembers(TSNamespaceSet nsset) {
            var ret = new List<(string interfaceName, string memberName, TSMemberDescription descr)>();
            foreach (var basename in Extends) {
                //TODO can we also make this work when it extends a generic interface? other types?
                var @base = nsset.FindTypeDescription(basename).description as TSInterfaceDescription;
                if (@base == null) { continue; } //might be a type alias; see AddInterfaceTo method
                var inheritedMembers = @base.InheritedMembers(nsset);
                foreach (var inheritedMember in inheritedMembers) {
                    if (@base.Members.Any(kvp => kvp.Key == inheritedMember.memberName && kvp.Value.Equals(inheritedMember.descr))) { continue; }
                    ret.Add(inheritedMember);
                }
                foreach (var member in @base.Members) {
                    ret.Add((basename, member.Key, member.Value));
                }
            }
            return ret.Distinct().ToList();
        }


        //TODO if an interface extends other interfaces, it's considered not equal to any other interface; ideally equality would involve checking that the sum of all inherited members are equal
        public override bool Equals(TSInterfaceDescription other) => Extends.None() && Members.SequenceEqual(other.Members) && Constructors.SequenceEqual(other.Constructors);

        public override int GetHashCode() {
            unchecked {
                int hash = 17;
                hash = hash * 486187739 + (int)Members.Product(x => x.GetHashCode());
                hash = hash * 486187739 + (int)Constructors.Product(x => x.GetHashCode());
                if (Extends != null) { hash = hash * 486187739 + (int)Extends.Product(x => x.GetHashCode()); }
                if (GenericParameters.Any()) { hash = hash * 486187739 + (int)GenericParameters.Product(x => x.GetHashCode()); }
                return hash;
            }
        }

        public void MakeFinal() {
            if (!IsClass) { throw new InvalidOperationException("Unable to make interface final"); }
            if (Constructors.Any(x => x.Parameters.None())) { throw new InvalidOperationException("Adding a private constructor will not make this class final; there is already a public constructor."); }
            if (Constructors.Any(x => x.Private && x.Parameters.None())) { return; }
            Constructors.Add(new TSMemberDescription() { Parameters = new List<KeyValuePair<string, TSParameterDescription>>(), Private = true });
        }
    }

    public class TSNamespaceDescription {
        public Dictionary<string, TSEnumDescription> Enums { get; } = new Dictionary<string, TSEnumDescription>();
        public Dictionary<string, TSInterfaceDescription> Interfaces { get; } = new Dictionary<string, TSInterfaceDescription>();
        public Dictionary<string, TSAliasDescription> Aliases { get; } = new Dictionary<string, TSAliasDescription>();
        public Dictionary<string, TSNamespaceDescription> Namespaces { get; } = new Dictionary<string, TSNamespaceDescription>();
        public JsDoc JsDoc { get; } = new JsDoc();

        public virtual IEnumerable<KeyValuePair<string, TSInterfaceDescription>> AllInterfaces => Interfaces;

        public HashSet<string> GetUsedTypes() {
            var types = new List<TSSimpleType>();
            AllInterfaces.Values().SelectMany(i => i.Members).SelectMany(x => x.Value.TypeParts()).AddRangeTo(types);
            Aliases.SelectMany(x => x.Value.TargetType.TypeParts()).AddRangeTo(types);
            types.RemoveAll(x => x.IsLiteralType);
            var ret = types.Select(x => x.FullName).ToHashSet();
            Namespaces.SelectMany(x => x.Value.GetUsedTypes()).AddRangeTo(ret);
            return ret;
        }

        private static string[] nameParser(string typename) {
            var ret = new List<string>();
            if ('<'.NotIn(typename)) {
                ret.Add(typename);
            } else {
                var generic = ParseTypeName(typename) as TSGenericType;
                ret.Add(generic.GenericDefinition.FullName);
                if (generic.Parameters.OfType<TSPlaceholder>().All(prm => prm.Default != null)) { ret.Add(generic.Name); } //since all the placeholders have default parameters, the type can also be used without generics
            }
            return ret.ToArray();
        }
        public HashSet<string> GetKnownTypes() {
            var ret = MiscExtensions.builtins.ToHashSet();
            Enums.Keys.AddRangeTo(ret);
            Interfaces.Keys.SelectMany(nameParser).AddRangeTo(ret);
            Aliases.Keys.SelectMany(nameParser).AddRangeTo(ret);
            if (this is TSRootNamespaceDescription root) {
                root.GlobalInterfaces.Keys.SelectMany(nameParser).AddRangeTo(ret);
                root.NominalTypes.SelectMany(nameParser).AddRangeTo(ret);
            }

            Namespaces.Values().SelectMany(x => x.GetKnownTypes()).AddRangeTo(ret);

            return ret.ToHashSet();
        }
        public HashSet<string> GetUndefinedTypes() {
            var ret = GetUsedTypes();
            ret.ExceptWith(GetKnownTypes());
            return ret;
        }

        public void ConsolidateMembers() {
            AllInterfaces.Values().ForEach(x => x.ConsolidateMembers());
            Namespaces.ForEachKVP((name, ns) => ns.ConsolidateMembers());
        }

        public TSNamespaceDescription GetNamespace(string path) {
            if (path.IsNullOrEmpty()) { return null; }
            var parts = path.Split('.');
            if (!Namespaces.TryGetValue(parts[0], out var next)) {
                next = new TSNamespaceDescription();
                Namespaces.Add(parts[0], next);
            }
            if (parts.Length == 1) { return next; }
            path = parts.Skip(1).Joined(".");
            return next.GetNamespace(path);
        }

        public bool IsEmpty => Enums.None() && AllInterfaces.None() && Aliases.None() && (Namespaces.None() || Namespaces.All(x => x.Value.IsEmpty));

        public List<(string usedType, string typename, string membername, string parametername)> GetTypeUsage() {
            string @null = null; //because null on its own doesn't provide type information when used on the right side of var
            var data = new List<(IEnumerable<TSSimpleType> typeParts, string typeName, string memberName, string parameterName)>();
            foreach (var (name, descr) in AllInterfaces) {
                foreach (var (memberName, memberDescr) in descr.Members) {
                    memberDescr.Parameters?.SelectKVP((parameterName, parameterDescr) => (parameterDescr.Type.TypeParts(), name, memberName, parameterName)).AddRangeTo(data);
                    data.Add((memberDescr.ReturnType.TypeParts(), name, memberName, @null));
                }
                descr.Extends.Select(x => (ParseTypeName(x).TypeParts(), name, @null, @null)).AddRangeTo(data);
                foreach (var ctor in descr.Constructors) {
                    ctor.Parameters?.SelectKVP((parameterName, parameterDescr) => (parameterDescr.Type.TypeParts(), name, "ctor", parameterName)).AddRangeTo(data);
                }
            }
            Aliases.SelectKVP((name, target) => (target.TargetType.TypeParts(), name, @null, @null)).AddRangeTo(data);

            var ret = data.SelectMany(x => x.typeParts.Select(y => (y.FullName, x.typeName, x.memberName, x.parameterName))).ToList();
            Namespaces.Values.SelectMany(x => x.GetTypeUsage()).AddRangeTo(ret);
            return ret;
        }
    }

    public class TSAliasDescription : EqualityBase<TSAliasDescription> {
        public ITSType TargetType { get; set; }
        public JsDoc JsDoc { get; } = new JsDoc();

        public override bool Equals(TSAliasDescription other) => TargetType.Equals(other.TargetType);
        public override int GetHashCode() => unchecked(17 * 486187739 + TargetType.GetHashCode());
    }

    public class TSRootNamespaceDescription : TSNamespaceDescription {
        public string Description { get; set; }
        public int MajorVersion { get; set; }
        public int MinorVersion { get; set; }
        public HashSet<string> Dependencies { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "interop" };

        public Dictionary<string, TSInterfaceDescription> GlobalInterfaces { get; } = new Dictionary<string, TSInterfaceDescription>();

        public HashSet<string> NominalTypes { get; } = new HashSet<string>();

        public override IEnumerable<KeyValuePair<string, TSInterfaceDescription>> AllInterfaces => Interfaces.Concat(GlobalInterfaces);

        public void AddMapType(string name, IEnumerable<string> typenames) => AddMapType(name, typenames.Select(x => (x, x)));
        public void AddMapType(string typename, IEnumerable<(string name, string returntypeName)> members) {
            var ret = new TSInterfaceDescription();
            members
                .Where(x => !x.returntypeName.Contains("<")) //exclude generic return types
                .Select(x => {
                    var member = new TSMemberDescription();
                    member.ReturnType = (TSSimpleType)x.returntypeName;
                    return KVP(x.name, member);
                })
                .AddRangeTo(ret.Members);
            var (ns, name) = SplitName(typename);
            if (ns.IsNullOrEmpty()) {
                GlobalInterfaces.Add(typename, ret);
            } else {
                Interfaces.Add(typename, ret);
            }
        }
    }

    public class TSNamespaceSet {
        public Dictionary<string, TSRootNamespaceDescription> Namespaces { get; } = new Dictionary<string, TSRootNamespaceDescription>();
        public HashSet<string> GetUsedTypes() => Namespaces.SelectMany(x => x.Value.GetUsedTypes()).ToHashSet();
        public HashSet<string> GetKnownTypes() => Namespaces.SelectMany(x => x.Value.GetKnownTypes()).ToHashSet();
        public HashSet<string> GetUndefinedTypes() {
            var ret = GetUsedTypes();
            ret.ExceptWith(GetKnownTypes());
            var singleCharTypes = ret.Where(x => x.Length == 1).ToList();
            ret.ExceptWith(singleCharTypes); //HACK no other way to differentiate between sequence<bool> and sequence<T> 
            return ret;
        }

        public TSNamespaceDescription GetNamespace(string path) {
            var (firstPart, rest) = FirstPathPart(path);
            if (!Namespaces.TryGetValue(firstPart, out var root)) {
                root = new TSRootNamespaceDescription();
                Namespaces.Add(firstPart, root);
            }
            if (rest.IsNullOrEmpty()) { return root; }
            return root.GetNamespace(rest);
        }

        public (string resolvedNamespace, string resolvedName, object description) FindTypeDescription(string fullname) {
            var (ns, name) = SplitName(fullname);

            if (ns.IsNullOrEmpty()) {
                foreach (var (rootNs, rootNsDescr) in Namespaces) {
                    if (rootNsDescr.GlobalInterfaces.TryGetValue(name, out var @interface)) {
                        return exitValue(@interface);
                    }
                    return exitValue(null);
                }
            }

            var nsDescr = GetNamespace(ns);
            if (nsDescr == null) { return exitValue(null); }

            //HACK we should standardize on the full name or the single name as the alias key
            if (nsDescr.Aliases.TryGetValue(fullname, out var aliasDescr) || nsDescr.Aliases.TryGetValue(name, out aliasDescr)) {
                if (aliasDescr.TargetType is TSSimpleType t) { return FindTypeDescription(t.FullName); }
                return exitValue(null);
            }

            //HACK we should standardize on the full name or the single name as the enum key
            if (nsDescr.Enums.TryGetValue(fullname, out var enumDescr) || nsDescr.Enums.TryGetValue(name, out enumDescr)) { return exitValue(enumDescr); }

            //interfaces are already standardized to use the full name
            if (nsDescr.Interfaces.TryGetValue(fullname, out var interfaceDescr)) { return exitValue(interfaceDescr); }

            return exitValue(null);

            (string, string, object) exitValue(object ret) => (ns, name, ret);
        }

        // this should be on TSNamespaceSet, so that each type will only be processed once
        public void FixBaseMemberConflicts() {
            var parsed = new HashSet<string>();
            var recursionDepth = 0;

            foreach (var ns in Namespaces) {
                parseNamespace(ns.Value);
            }

            void parseNamespace(TSNamespaceDescription ns) {
                foreach (var ns1 in ns.Namespaces) {
                    parseNamespace(ns1.Value);
                }
                foreach (var kvp in ns.Interfaces) {
                    fixInterface(kvp);
                }
                if (ns is TSRootNamespaceDescription rootNs) {
                    foreach (var kvp in rootNs.GlobalInterfaces) {
                        fixInterface(kvp);
                    }
                }
            }
            void fixInterface(KeyValuePair<string, TSInterfaceDescription> kvp) {
                if (kvp.Value.Extends.None()) { return; }
                if (parsed.Contains(kvp.Key)) { return; }

                parsed.Add(kvp.Key);
                Debug.Print(Repeat("   ", recursionDepth).Joined("") + kvp.Key);

                foreach (var @baseName in kvp.Value.Extends) {
                    var @interface = FindTypeDescription(baseName).description as TSInterfaceDescription;
                    if (@interface == null) { continue; }
                    recursionDepth += 1;
                    fixInterface(KVP(baseName, @interface));
                    recursionDepth -= 1;
                }

                var members = kvp.AllMembers(this);
                var lookup = members.ToLookup(x => x.memberName);
                var addedMembers = false;
                foreach (var grp in lookup) {
                    var items = grp.Distinct().ToList();
                    if (items.Count() == 1) { continue; }
                    var signatures = items.ToLookup(x => x.descr);
                    if (signatures.Count == 1) { continue; } // all methods have the same signature
                    var found = false;
                    foreach (var sourceInterface in items.Select(x => x.interfaceName).ToHashSet()) {
                        if (signatures.All(signatureGroup => signatureGroup.Any(x => x.interfaceName == sourceInterface))) { //make sure all the signatures appear together in at least one interface
                            found = true;
                            break;
                        }
                    }
                    if (found) { continue; }

                    var toAddMultiple = signatures
                        .Select(signatureGroup => KVP(grp.First().memberName, signatureGroup.Key.Clone()))
                        .Where(x => kvp.Value.Members.None(y => x.Key == y.Key && x.Value.Equals(y.Value))).ToList();
                    foreach (var item in toAddMultiple) {
                        if (kvp.Value.Members.Any(x => item.Key == x.Key && item.Value.Equals(x.Value))) { continue; }
                        if (item.Value.Parameters == null) { continue; } //there's no point in trying to copy properties down the inheritance chain; Typescript doesn't allow multiple overloads on properties
                        Debug.Print(Repeat("    ", recursionDepth + 1).Joined("") + item.Key);
                        kvp.Value.Members.Add(item);
                    }
                    addedMembers = true;
                }
                if (addedMembers) { kvp.Value.ConsolidateMembers(); }
            }
        }

        public void ConsolidateMembers() => Namespaces.ForEachKVP((key, ns) => ns.ConsolidateMembers());
    }

    public class TSParameterListComparer : IEqualityComparer<List<KeyValuePair<string, TSParameterDescription>>> {
        public bool Equals(List<KeyValuePair<string, TSParameterDescription>> x, List<KeyValuePair<string, TSParameterDescription>> y) {
            if (x == null) { return y == null; }
            return x.SequenceEqual(y);
        }
        public int GetHashCode(List<KeyValuePair<string, TSParameterDescription>> obj) {
            unchecked {
                return obj.Aggregate(17, (hash, x) => hash * 486187739 + x.GetHashCode());
            }
        }
    }

    public class NamespaceOutput {
        public TSRootNamespaceDescription RootNamespace { get; set; }
        public string NominalTypes { get; set; }
        public string LocalTypes { get; set; }
        public string MainFile => NominalTypes + LocalTypes;
        public string TestsFile { get; set; }
        //TODO if there are other declarations that should not be repeated,they should also be removed from the LocalTypes property and placed in NominalTypes and MergedTypes properties (which would be named something else, of course)
        public string MergedNominalTypes { get; set; } //same across NamespaceSet
    }
}