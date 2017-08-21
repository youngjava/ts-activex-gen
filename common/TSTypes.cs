﻿using System.Linq;
using System.Collections.Generic;
using static System.Linq.Enumerable;

namespace TsActivexGen {
    /// <summary>Describes namespace+name types, literal types, built-ins, and open generic types</summary>
    public class TSSimpleType : ITSType {
        public static readonly TSSimpleType Any = "any";
        public static readonly TSSimpleType Void = "void";
        public static readonly TSSimpleType Undefined = "undefined";
        public static readonly TSSimpleType Number = "number";
        public static readonly TSSimpleType String = "string";

        public string FullName { get; }
        public string Namespace {
            get {
                if (IsLiteralType) { return ""; }
                var parts = FullName.Split('.');
                if (parts.Length == 1) { return ""; }
                return parts[0];
            }
        }
        public bool IsLiteralType => Functions.IsLiteralTypeName(FullName);

        public IEnumerable<TSSimpleType> TypeParts() => new[] { this };
        public bool Equals(ITSType other) => other is TSSimpleType x && FullName == x.FullName;

        public TSSimpleType(string fullName = null) {
            //TODO throw an exception on non-open generic types -- e.g. SafeArray<int>; SafeArray<> should be allowed
            FullName = fullName.Trim();
        }

        public static implicit operator string(TSSimpleType t) => t.FullName;
        public static implicit operator TSSimpleType(string s) => new TSSimpleType(s);

        public override string ToString() => FullName;
    }

    public class TSTupleType : ITSType {
        public List<ITSType> Members { get; } = new List<ITSType>();

        public IEnumerable<TSSimpleType> TypeParts() => Members.SelectMany(x => x.TypeParts());
        public bool Equals(ITSType other) => other is TSTupleType x && Members.SequenceEqual(x.Members);

        public TSTupleType() { }
        public TSTupleType(IEnumerable<string> members) => Members = members.Select(x => new TSSimpleType(x)).Cast<ITSType>().ToList();

        public override string ToString() => "[" + Members.Joined(",") + "]";
    }

    public class TSObjectType : ITSType {
        public static readonly TSObjectType PlainObject = new TSObjectType(Empty<KeyValuePair<string, (ITSType, bool)>>());

        public Dictionary<string, (ITSType type, bool @readonly)> Members { get; } = new Dictionary<string, (ITSType type, bool @readonly)>();

        public bool Equals(ITSType other) => other is TSObjectType x && Members.OrderBy(y => y.Key).ToList().SequenceEqual(x.Members.OrderBy(y => y.Key).ToList());
        public IEnumerable<TSSimpleType> TypeParts() => Members.Values.SelectMany(x => x.type.TypeParts());

        public TSObjectType(IEnumerable<KeyValuePair<string, (ITSType type, bool @readonly)>> members) => Members = members.ToDictionary();

        public override string ToString() => $"{Members.Keys.Joined(",", x => $".{x}")}";
    }

    public class TSFunctionType : ITSType {
        public TSMemberDescription FunctionDescription { get; }

        public bool Equals(ITSType other) => other is TSFunctionType x && FunctionDescription.Equals(x.FunctionDescription);
        public IEnumerable<TSSimpleType> TypeParts() => FunctionDescription.TypeParts();

        public TSFunctionType(TSMemberDescription fn) => FunctionDescription = fn;

        public override string ToString() => $"({FunctionDescription.Parameters.Keys().Joined(",")}) => {FunctionDescription.ReturnType}";
    }

    public class TSUnionType : ITSType {
        public HashSet<ITSType> Parts { get; } = new HashSet<ITSType>();

        //TODO implement this as an operator overload on +=
        public void AddPart(ITSType part) {
            if (part is TSUnionType x) {
                x.Parts.AddRangeTo(Parts);
            } else {
                Parts.Add(part);
            }
        }

        public bool Equals(ITSType other) => other is TSUnionType x && Parts.SequenceEqual(x.Parts);
        public IEnumerable<TSSimpleType> TypeParts() => Parts.SelectMany(x => x.TypeParts());

        public override string ToString() => Parts.Joined("|");
    }

    public class TSPlaceholder : ITSType {
        public string Name { get; set; }

        public bool Equals(ITSType other) => other is TSPlaceholder x && x.Name == Name;

        public IEnumerable<TSSimpleType> TypeParts() => Empty<TSSimpleType>();

        public override string ToString() => Name;
    }

    public class TSGenericType : ITSType {
        public string Name { get; set; }
        public List<ITSType> Parameters { get; } = new List<ITSType>();  //In order to refer to the same type parameter twice, use two TSPlaceholder types with the same name

        public bool Equals(ITSType other) => other is TSGenericType x && Parameters.SequenceEqual(x.Parameters);

        public IEnumerable<TSSimpleType> TypeParts() {
            yield return GenericDefinition;
            foreach (var part in Parameters.SelectMany(x => x.TypeParts())) {
                yield return part;
            }
        }

        public override string ToString() => $"{Name}<{Parameters.Joined(",", x => x is TSPlaceholder ? null : x?.ToString())}>";

        public TSSimpleType GenericDefinition => $"{Name}<{Parameters.Joined(",", x => "")}>";
    }
}
