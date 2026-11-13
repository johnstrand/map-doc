using MapDoc.Core.Models;

namespace MapDoc.Core.Presets;

public static class PresetFunctoids
{
    private static Guid DeterministicGuid(int index) =>
        Guid.Parse($"10000000-0000-4000-8000-{index:D12}");

    public static IReadOnlyList<FunctoidDefinition> All { get; } = CreateAll();

    private static List<FunctoidDefinition> CreateAll()
    {
        var epoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var list = new List<FunctoidDefinition>
        {
            // ==================== CONVERSION ====================
            new()
            {
                Id = DeterministicGuid(1),
                PresetKey = "preset.tostring",
                IsPreset = true,
                Name = "ToString",
                Category = FunctoidCategory.Conversion,
                Symbol = "→S",
                Description = "Converts any scalar value (number, boolean, formatted string) to a plain string.",
                Inputs = [Input(0, "value", PortTypeSpec.AnyScalar(), "Scalar value to convert to string.")],
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarString())
            },
            new()
            {
                Id = DeterministicGuid(2),
                PresetKey = "preset.toint32",
                IsPreset = true,
                Name = "ToInt32",
                Category = FunctoidCategory.Conversion,
                Symbol = "→I32",
                Description = "Parses or converts a scalar value to a 32-bit integer.",
                Inputs = [Input(0, "value", PortTypeSpec.AnyScalar(), "Scalar value to convert.")],
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarNumber(NumberSubtype.Int32))
            },
            new()
            {
                Id = DeterministicGuid(3),
                PresetKey = "preset.toint64",
                IsPreset = true,
                Name = "ToInt64",
                Category = FunctoidCategory.Conversion,
                Symbol = "→I64",
                Description = "Parses or converts a scalar value to a 64-bit integer.",
                Inputs = [Input(0, "value", PortTypeSpec.AnyScalar(), "Scalar value to convert.")],
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarNumber(NumberSubtype.Int64))
            },
            new()
            {
                Id = DeterministicGuid(4),
                PresetKey = "preset.todecimal",
                IsPreset = true,
                Name = "ToDecimal",
                Category = FunctoidCategory.Conversion,
                Symbol = "→Dec",
                Description = "Parses or converts a scalar value to an exact decimal number.",
                Inputs = [Input(0, "value", PortTypeSpec.AnyScalar(), "Scalar value to convert.")],
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarNumber(NumberSubtype.Decimal))
            },
            new()
            {
                Id = DeterministicGuid(5),
                PresetKey = "preset.todouble",
                IsPreset = true,
                Name = "ToDouble",
                Category = FunctoidCategory.Conversion,
                Symbol = "→Dbl",
                Description = "Parses or converts a scalar value to a 64-bit floating-point number.",
                Inputs = [Input(0, "value", PortTypeSpec.AnyScalar(), "Scalar value to convert.")],
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarNumber(NumberSubtype.Double))
            },
            new()
            {
                Id = DeterministicGuid(6),
                PresetKey = "preset.toboolean",
                IsPreset = true,
                Name = "ToBoolean",
                Category = FunctoidCategory.Conversion,
                Symbol = "→B",
                Description = "Converts a scalar value (e.g., 'true'/'false', 1/0) to boolean.",
                Inputs = [Input(0, "value", PortTypeSpec.AnyScalar(), "Scalar value to convert.")],
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarBoolean())
            },
            new()
            {
                Id = DeterministicGuid(7),
                PresetKey = "preset.formatdate",
                IsPreset = true,
                Name = "FormatDate",
                Category = FunctoidCategory.Conversion,
                Symbol = "📅→S",
                Description = "Formats a date, time, or datetime value into a string using a format pattern.",
                Inputs = [Input(0, "value", PortTypeSpec.Of(TypeDescriptor.ScalarString()), "Date/datetime input.")],
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarString()),
                ConfigParams =
                [
                    new() { Key = "format", Label = "Format Pattern", Placeholder = "yyyy-MM-dd", DefaultValue = "yyyy-MM-dd", Description = "ISO or custom date/time format string." }
                ]
            },
            new()
            {
                Id = DeterministicGuid(8),
                PresetKey = "preset.parsedate",
                IsPreset = true,
                Name = "ParseDate",
                Category = FunctoidCategory.Conversion,
                Symbol = "S→📅",
                Description = "Parses a string representation into a Date subtype.",
                Inputs = [Input(0, "text", PortTypeSpec.Of(TypeDescriptor.ScalarString()), "Date string to parse.")],
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarString(StringSubtype.Date)),
                ConfigParams =
                [
                    new() { Key = "format", Label = "Source Format", Placeholder = "yyyy-MM-dd", DefaultValue = "", Description = "Expected input date format (optional if ISO-8601)." }
                ]
            },
            new()
            {
                Id = DeterministicGuid(9),
                PresetKey = "preset.parsedatetime",
                IsPreset = true,
                Name = "ParseDateTime",
                Category = FunctoidCategory.Conversion,
                Symbol = "S→🕒",
                Description = "Parses a string or epoch scalar into a DateTime subtype.",
                Inputs = [Input(0, "value", PortTypeSpec.AnyScalar(), "String or timestamp value.")],
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarString(StringSubtype.DateTime))
            },
            new()
            {
                Id = DeterministicGuid(10),
                PresetKey = "preset.parseuuid",
                IsPreset = true,
                Name = "ParseUuid",
                Category = FunctoidCategory.Conversion,
                Symbol = "S→ID",
                Description = "Validates/converts a string into a UUID subtype.",
                Inputs = [Input(0, "text", PortTypeSpec.Of(TypeDescriptor.ScalarString()), "UUID string.")],
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarString(StringSubtype.Uuid))
            },

            // ==================== STRING ====================
            new()
            {
                Id = DeterministicGuid(11),
                PresetKey = "preset.concat",
                IsPreset = true,
                Name = "Concat",
                Category = FunctoidCategory.String,
                Symbol = "⊕",
                Description = "Concatenates two or more scalar values into a single string with an optional separator.",
                Inputs =
                [
                    Input(0, "part1", PortTypeSpec.AnyScalar(), "First part."),
                    Input(1, "part2", PortTypeSpec.AnyScalar(), "Second part.")
                ],
                IsVariadic = true,
                VariadicInputName = "part",
                VariadicInputType = PortTypeSpec.AnyScalar(),
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarString()),
                ConfigParams =
                [
                    new() { Key = "separator", Label = "Separator", Placeholder = "(empty)", DefaultValue = "", Description = "Optional delimiter inserted between parts." }
                ]
            },
            new()
            {
                Id = DeterministicGuid(12),
                PresetKey = "preset.substring",
                IsPreset = true,
                Name = "Substring",
                Category = FunctoidCategory.String,
                Symbol = "✂",
                Description = "Extracts a substring starting at an index and optional length.",
                Inputs =
                [
                    Input(0, "text", PortTypeSpec.Of(TypeDescriptor.ScalarString()), "Source string."),
                    Input(1, "startIndex", PortTypeSpec.Of(TypeDescriptor.ScalarNumber(NumberSubtype.Int32)), "0-based start index.", isOptional: true),
                    Input(2, "length", PortTypeSpec.Of(TypeDescriptor.ScalarNumber(NumberSubtype.Int32)), "Maximum character length.", isOptional: true)
                ],
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarString()),
                ConfigParams =
                [
                    new() { Key = "range", Label = "Fixed Start / Length", Placeholder = "0, 10", DefaultValue = "", Description = "Optional literal start,length when pins are left unwired." }
                ]
            },
            new()
            {
                Id = DeterministicGuid(13),
                PresetKey = "preset.trim",
                IsPreset = true,
                Name = "Trim",
                Category = FunctoidCategory.String,
                Symbol = "⇥⇤",
                Description = "Removes leading and trailing whitespace from a string.",
                Inputs = [Input(0, "text", PortTypeSpec.Of(TypeDescriptor.ScalarString()), "String to trim.")],
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarString())
            },
            new()
            {
                Id = DeterministicGuid(14),
                PresetKey = "preset.uppercase",
                IsPreset = true,
                Name = "UpperCase",
                Category = FunctoidCategory.String,
                Symbol = "AA",
                Description = "Converts a string to uppercase.",
                Inputs = [Input(0, "text", PortTypeSpec.Of(TypeDescriptor.ScalarString()), "Input string.")],
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarString())
            },
            new()
            {
                Id = DeterministicGuid(15),
                PresetKey = "preset.lowercase",
                IsPreset = true,
                Name = "LowerCase",
                Category = FunctoidCategory.String,
                Symbol = "aa",
                Description = "Converts a string to lowercase.",
                Inputs = [Input(0, "text", PortTypeSpec.Of(TypeDescriptor.ScalarString()), "Input string.")],
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarString())
            },
            new()
            {
                Id = DeterministicGuid(16),
                PresetKey = "preset.replace",
                IsPreset = true,
                Name = "Replace",
                Category = FunctoidCategory.String,
                Symbol = "⇄",
                Description = "Replaces occurrences of a pattern in a string.",
                Inputs = [Input(0, "text", PortTypeSpec.Of(TypeDescriptor.ScalarString()), "Source string.")],
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarString()),
                ConfigParams =
                [
                    new() { Key = "find", Label = "Find", Placeholder = "old", DefaultValue = "", Description = "Substring or regex to match.", IsRequired = true },
                    new() { Key = "replaceWith", Label = "Replace With", Placeholder = "new", DefaultValue = "", Description = "Replacement text." }
                ]
            },
            new()
            {
                Id = DeterministicGuid(17),
                PresetKey = "preset.stringlength",
                IsPreset = true,
                Name = "StringLength",
                Category = FunctoidCategory.String,
                Symbol = "|s|",
                Description = "Returns the character count of a string.",
                Inputs = [Input(0, "text", PortTypeSpec.Of(TypeDescriptor.ScalarString()), "Input string.")],
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarNumber(NumberSubtype.Int32))
            },
            new()
            {
                Id = DeterministicGuid(18),
                PresetKey = "preset.split",
                IsPreset = true,
                Name = "Split",
                Category = FunctoidCategory.String,
                Symbol = "S→[]",
                Description = "Splits a delimited string into an array of strings.",
                Inputs = [Input(0, "text", PortTypeSpec.Of(TypeDescriptor.ScalarString()), "Delimited input string.")],
                OutputType = PortTypeSpec.Of(TypeDescriptor.ArrayOf(TypeDescriptor.ScalarString())),
                ConfigParams =
                [
                    new() { Key = "delimiter", Label = "Delimiter", Placeholder = ",", DefaultValue = ",", Description = "Delimiter string.", IsRequired = true }
                ]
            },

            // ==================== MATH ====================
            new()
            {
                Id = DeterministicGuid(19),
                PresetKey = "preset.add",
                IsPreset = true,
                Name = "Add",
                Category = FunctoidCategory.Math,
                Symbol = "+",
                Description = "Adds two or more numeric values.",
                Inputs =
                [
                    Input(0, "a", PortTypeSpec.Of(TypeDescriptor.ScalarNumber()), "First number."),
                    Input(1, "b", PortTypeSpec.Of(TypeDescriptor.ScalarNumber()), "Second number.")
                ],
                IsVariadic = true,
                VariadicInputName = "term",
                VariadicInputType = PortTypeSpec.Of(TypeDescriptor.ScalarNumber()),
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarNumber(NumberSubtype.Decimal))
            },
            new()
            {
                Id = DeterministicGuid(20),
                PresetKey = "preset.subtract",
                IsPreset = true,
                Name = "Subtract",
                Category = FunctoidCategory.Math,
                Symbol = "−",
                Description = "Subtracts b from a.",
                Inputs =
                [
                    Input(0, "a", PortTypeSpec.Of(TypeDescriptor.ScalarNumber()), "Minuend."),
                    Input(1, "b", PortTypeSpec.Of(TypeDescriptor.ScalarNumber()), "Subtrahend.")
                ],
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarNumber(NumberSubtype.Decimal))
            },
            new()
            {
                Id = DeterministicGuid(21),
                PresetKey = "preset.multiply",
                IsPreset = true,
                Name = "Multiply",
                Category = FunctoidCategory.Math,
                Symbol = "×",
                Description = "Multiplies two or more numeric values (e.g., Quantity * UnitPrice).",
                Inputs =
                [
                    Input(0, "a", PortTypeSpec.Of(TypeDescriptor.ScalarNumber()), "First factor."),
                    Input(1, "b", PortTypeSpec.Of(TypeDescriptor.ScalarNumber()), "Second factor.")
                ],
                IsVariadic = true,
                VariadicInputName = "factor",
                VariadicInputType = PortTypeSpec.Of(TypeDescriptor.ScalarNumber()),
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarNumber(NumberSubtype.Decimal))
            },
            new()
            {
                Id = DeterministicGuid(22),
                PresetKey = "preset.divide",
                IsPreset = true,
                Name = "Divide",
                Category = FunctoidCategory.Math,
                Symbol = "÷",
                Description = "Divides numerator by denominator.",
                Inputs =
                [
                    Input(0, "numerator", PortTypeSpec.Of(TypeDescriptor.ScalarNumber()), "Numerator."),
                    Input(1, "denominator", PortTypeSpec.Of(TypeDescriptor.ScalarNumber()), "Denominator.")
                ],
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarNumber(NumberSubtype.Decimal))
            },
            new()
            {
                Id = DeterministicGuid(23),
                PresetKey = "preset.round",
                IsPreset = true,
                Name = "Round",
                Category = FunctoidCategory.Math,
                Symbol = "≈",
                Description = "Rounds a numeric value to a specified number of decimal places.",
                Inputs = [Input(0, "value", PortTypeSpec.Of(TypeDescriptor.ScalarNumber()), "Value to round.")],
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarNumber(NumberSubtype.Decimal)),
                ConfigParams =
                [
                    new() { Key = "decimals", Label = "Decimal Places", Placeholder = "2", DefaultValue = "2", Description = "Number of fractional digits." }
                ]
            },

            // ==================== LOGICAL & CONDITIONAL ====================
            new()
            {
                Id = DeterministicGuid(24),
                PresetKey = "preset.equals",
                IsPreset = true,
                Name = "Equals",
                Category = FunctoidCategory.Logical,
                Symbol = "==",
                Description = "Returns true when two scalar inputs are equal.",
                Inputs =
                [
                    Input(0, "left", PortTypeSpec.AnyScalar(), "Left value."),
                    Input(1, "right", PortTypeSpec.AnyScalar(), "Right value (optional if compared to literal).", isOptional: true)
                ],
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarBoolean()),
                ConfigParams =
                [
                    new() { Key = "compareTo", Label = "Compare Literal", Placeholder = "(optional)", DefaultValue = "", Description = "Literal value to compare against when right pin is unwired." }
                ]
            },
            new()
            {
                Id = DeterministicGuid(25),
                PresetKey = "preset.and",
                IsPreset = true,
                Name = "LogicalAnd",
                Category = FunctoidCategory.Logical,
                Symbol = "&&",
                Description = "Logical AND across two or more boolean inputs.",
                Inputs =
                [
                    Input(0, "a", PortTypeSpec.Of(TypeDescriptor.ScalarBoolean()), "First condition."),
                    Input(1, "b", PortTypeSpec.Of(TypeDescriptor.ScalarBoolean()), "Second condition.")
                ],
                IsVariadic = true,
                VariadicInputName = "cond",
                VariadicInputType = PortTypeSpec.Of(TypeDescriptor.ScalarBoolean()),
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarBoolean())
            },
            new()
            {
                Id = DeterministicGuid(26),
                PresetKey = "preset.or",
                IsPreset = true,
                Name = "LogicalOr",
                Category = FunctoidCategory.Logical,
                Symbol = "||",
                Description = "Logical OR across two or more boolean inputs.",
                Inputs =
                [
                    Input(0, "a", PortTypeSpec.Of(TypeDescriptor.ScalarBoolean()), "First condition."),
                    Input(1, "b", PortTypeSpec.Of(TypeDescriptor.ScalarBoolean()), "Second condition.")
                ],
                IsVariadic = true,
                VariadicInputName = "cond",
                VariadicInputType = PortTypeSpec.Of(TypeDescriptor.ScalarBoolean()),
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarBoolean())
            },
            new()
            {
                Id = DeterministicGuid(27),
                PresetKey = "preset.not",
                IsPreset = true,
                Name = "LogicalNot",
                Category = FunctoidCategory.Logical,
                Symbol = "!",
                Description = "Negates a boolean input.",
                Inputs = [Input(0, "condition", PortTypeSpec.Of(TypeDescriptor.ScalarBoolean()), "Boolean value to invert.")],
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarBoolean())
            },
            new()
            {
                Id = DeterministicGuid(28),
                PresetKey = "preset.ifthenelse",
                IsPreset = true,
                Name = "IfThenElse",
                Category = FunctoidCategory.Logical,
                Symbol = "?:",
                Description = "Selects 'whenTrue' if condition is true, otherwise 'whenFalse'.",
                Inputs =
                [
                    Input(0, "condition", PortTypeSpec.Of(TypeDescriptor.ScalarBoolean()), "Boolean condition."),
                    Input(1, "whenTrue", PortTypeSpec.Any(), "Value emitted when true."),
                    Input(2, "whenFalse", PortTypeSpec.Any(), "Value emitted when false.")
                ],
                OutputType = PortTypeSpec.Any(),
                OutputInference = OutputInferenceRule.SameAsInputPin,
                OutputInferencePinIndex = 1
            },
            new()
            {
                Id = DeterministicGuid(29),
                PresetKey = "preset.valuemapping",
                IsPreset = true,
                Name = "ValueMapping",
                Category = FunctoidCategory.Logical,
                Symbol = "✓→",
                Description = "BizTalk Value Mapping: emits the second parameter only when the first boolean parameter is true.",
                Inputs =
                [
                    Input(0, "condition", PortTypeSpec.Of(TypeDescriptor.ScalarBoolean()), "Guard boolean condition."),
                    Input(1, "value", PortTypeSpec.Any(), "Value to map when condition holds.")
                ],
                OutputType = PortTypeSpec.Any(),
                OutputInference = OutputInferenceRule.SameAsInputPin,
                OutputInferencePinIndex = 1
            },
            new()
            {
                Id = DeterministicGuid(30),
                PresetKey = "preset.coalesce",
                IsPreset = true,
                Name = "Coalesce",
                Category = FunctoidCategory.Logical,
                Symbol = "??",
                Description = "Returns the first non-null/present input (or configured fallback literal), resolving nullable/optional warnings.",
                Inputs =
                [
                    Input(0, "primary", PortTypeSpec.Any(), "Nullable or optional primary value."),
                    Input(1, "fallback", PortTypeSpec.Any(), "Fallback value when primary is null/missing.", isOptional: true)
                ],
                IsVariadic = true,
                VariadicInputName = "fallback",
                VariadicInputType = PortTypeSpec.Any(),
                OutputType = PortTypeSpec.Any(),
                OutputInference = OutputInferenceRule.SameAsInputPin,
                OutputInferencePinIndex = 0,
                ConfigParams =
                [
                    new() { Key = "defaultLiteral", Label = "Default Literal", Placeholder = "N/A", DefaultValue = "", Description = "Fallback constant if no fallback pin is wired." }
                ]
            },

            // ==================== GENERATORS (0 INPUTS) ====================
            new()
            {
                Id = DeterministicGuid(31),
                PresetKey = "preset.constant",
                IsPreset = true,
                Name = "Constant",
                Category = FunctoidCategory.Generators,
                Symbol = "K",
                Description = "Outputs a constant scalar value configured on the functoid (0 inputs).",
                Inputs = [],
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarString()),
                OutputInference = OutputInferenceRule.FromConstantConfig,
                ConfigParams =
                [
                    new() { Key = "value", Label = "Constant Value", Placeholder = "USD", DefaultValue = "", Description = "Literal value emitted by this functoid.", IsRequired = true },
                    new() { Key = "type", Label = "Scalar Type", Placeholder = "string | int32 | decimal | boolean | uuid | date | datetime", DefaultValue = "string", Description = "Output scalar type (string, int32, int64, double, decimal, boolean, uuid, date, datetime)." }
                ]
            },
            new()
            {
                Id = DeterministicGuid(32),
                PresetKey = "preset.utcnow",
                IsPreset = true,
                Name = "UtcNow",
                Category = FunctoidCategory.Generators,
                Symbol = "🕒",
                Description = "Generates the current UTC timestamp (0 inputs).",
                Inputs = [],
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarString(StringSubtype.DateTime))
            },
            new()
            {
                Id = DeterministicGuid(33),
                PresetKey = "preset.today",
                IsPreset = true,
                Name = "Today",
                Category = FunctoidCategory.Generators,
                Symbol = "📅",
                Description = "Generates the current calendar date (0 inputs).",
                Inputs = [],
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarString(StringSubtype.Date))
            },
            new()
            {
                Id = DeterministicGuid(34),
                PresetKey = "preset.newuuid",
                IsPreset = true,
                Name = "NewUuid",
                Category = FunctoidCategory.Generators,
                Symbol = "ID",
                Description = "Generates a new v4/v7 UUID (0 inputs).",
                Inputs = [],
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarString(StringSubtype.Uuid))
            },

            // ==================== COLLECTION / ARRAY ====================
            new()
            {
                Id = DeterministicGuid(35),
                PresetKey = "preset.count",
                IsPreset = true,
                Name = "Count",
                Category = FunctoidCategory.Collection,
                Symbol = "#",
                Description = "Returns the number of elements in an array.",
                Inputs = [Input(0, "array", PortTypeSpec.AnyArray(), "Source array to count.")],
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarNumber(NumberSubtype.Int32)),
                AggregatesRepeatingInput = true
            },
            new()
            {
                Id = DeterministicGuid(36),
                PresetKey = "preset.first",
                IsPreset = true,
                Name = "First",
                Category = FunctoidCategory.Collection,
                Symbol = "[0]",
                Description = "Extracts the first item of an array or repeating field.",
                Inputs = [Input(0, "items", PortTypeSpec.Any(), "Source array or repeating item property.")],
                OutputType = PortTypeSpec.Any(),
                OutputInference = OutputInferenceRule.ElementOfArrayInputPin,
                OutputInferencePinIndex = 0,
                AggregatesRepeatingInput = true
            },
            new()
            {
                Id = DeterministicGuid(37),
                PresetKey = "preset.last",
                IsPreset = true,
                Name = "Last",
                Category = FunctoidCategory.Collection,
                Symbol = "[-1]",
                Description = "Extracts the last item of an array or repeating field.",
                Inputs = [Input(0, "items", PortTypeSpec.Any(), "Source array or repeating item property.")],
                OutputType = PortTypeSpec.Any(),
                OutputInference = OutputInferenceRule.ElementOfArrayInputPin,
                OutputInferencePinIndex = 0,
                AggregatesRepeatingInput = true
            },
            new()
            {
                Id = DeterministicGuid(38),
                PresetKey = "preset.join",
                IsPreset = true,
                Name = "Join",
                Category = FunctoidCategory.Collection,
                Symbol = "[]→S",
                Description = "Joins an array or repeating scalar field into a delimited string.",
                Inputs = [Input(0, "items", PortTypeSpec.Any(), "Array or repeating scalar property to join.")],
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarString()),
                AggregatesRepeatingInput = true,
                ConfigParams =
                [
                    new() { Key = "separator", Label = "Separator", Placeholder = ", ", DefaultValue = ", ", Description = "Delimiter inserted between joined elements." }
                ]
            },
            new()
            {
                Id = DeterministicGuid(39),
                PresetKey = "preset.sum",
                IsPreset = true,
                Name = "Sum",
                Category = FunctoidCategory.Collection,
                Symbol = "∑",
                Description = "Sums an array of numbers or a repeating numeric property across items.",
                Inputs = [Input(0, "values", PortTypeSpec.Any(), "Numeric array or repeating numeric field.")],
                OutputType = PortTypeSpec.Of(TypeDescriptor.ScalarNumber(NumberSubtype.Decimal)),
                AggregatesRepeatingInput = true
            },
            new()
            {
                Id = DeterministicGuid(40),
                PresetKey = "preset.toarray",
                IsPreset = true,
                Name = "ToArray",
                Category = FunctoidCategory.Collection,
                Symbol = "→[]",
                Description = "Wraps one or more values into an array.",
                Inputs = [Input(0, "item1", PortTypeSpec.Any(), "First element.")],
                IsVariadic = true,
                VariadicInputName = "item",
                VariadicInputType = PortTypeSpec.Any(),
                OutputType = PortTypeSpec.AnyArray(),
                OutputInference = OutputInferenceRule.ArrayOfInputPin,
                OutputInferencePinIndex = 0
            }
        };

        foreach (var f in list)
        {
            f.CreatedAtUtc = epoch;
            f.UpdatedAtUtc = epoch;
        }

        return list;
    }

    private static FunctoidInputSpec Input(int index, string name, PortTypeSpec typeSpec, string description, bool isOptional = false) =>
        new()
        {
            Index = index,
            Name = name,
            TypeSpec = typeSpec,
            Description = description,
            IsOptional = isOptional
        };
}
