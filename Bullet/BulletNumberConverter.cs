using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>在加载时将小数或受限常量表达式直接转换为有限数值。</summary>
public sealed class BulletNumberConverter : JsonConverter<double>
{
    /// <summary>读取数值或包含PI、TAU及四则运算的字符串。</summary>
    /// <param name="reader">当前JSON读取器。</param>
    /// <param name="typeToConvert">目标小数类型。</param>
    /// <param name="options">当前反序列化选项。</param>
    /// <returns>已求值的有限数值，单位由属性决定。</returns>
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // 解析状态仅存在于本次读取，配置对象不保存表达式。
        if (reader.TokenType == JsonTokenType.Number) return Finite(reader.GetDouble());
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("需要数值或表达式字符串。");
        string expression = reader.GetString()!;
        if (expression.Length > 1024) throw new JsonException("表达式不能超过1024字符。");
        int offset = 0;
        double result = Parse(expression, ref offset, 0, 0);
        SkipSpace(expression, ref offset);
        if (offset != expression.Length) throw new JsonException($"表达式位置{offset}存在无效内容。");
        return result;
    }

    /// <summary>将已解析的小数保存为JSON数值。</summary>
    /// <param name="writer">JSON写入器。</param>
    /// <param name="value">有限数值。</param>
    /// <param name="options">序列化选项。</param>
    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
        => writer.WriteNumberValue(Finite(value));

    /// <summary>按优先级解析四则运算，限制递归深度。</summary>
    /// <param name="text">常量表达式。</param>
    /// <param name="offset">当前字符位置，返回已消费位置。</param>
    /// <param name="minimum">最低运算优先级。</param>
    /// <param name="depth">递归层数，最多64。</param>
    /// <returns>有限运算结果。</returns>
    private static double Parse(string text, ref int offset, int minimum, int depth)
    {
        if (depth > 64) throw new JsonException("表达式嵌套过深。");
        SkipSpace(text, ref offset);
        if (offset >= text.Length) throw new JsonException("表达式缺少数值。");
        // 先读取一元运算、括号、常量或数字。
        double left;
        char token = text[offset];
        if (token == '+' || token == '-')
        {
            offset++;
            left = Parse(text, ref offset, 3, depth + 1) * (token == '-' ? -1 : 1);
        }
        else if (token == '(')
        {
            offset++;
            left = Parse(text, ref offset, 0, depth + 1);
            SkipSpace(text, ref offset);
            if (offset >= text.Length || text[offset++] != ')') throw new JsonException("表达式缺少右括号。");
        }
        else if (char.IsLetter(token))
        {
            // 常量名称严格区分大小写，不接受变量和函数。
            int start = offset;
            while (offset < text.Length && char.IsLetter(text[offset])) offset++;
            left = text[start..offset] switch
            {
                "PI" => Math.PI,
                "TAU" => Math.Tau,
                _ => throw new JsonException("只支持PI和TAU常量。")
            };
        }
        else
        {
            // 数字支持小数点与科学计数法，使用固定文化解析。
            int start = offset;
            while (offset < text.Length && (char.IsAsciiDigit(text[offset]) || text[offset] == '.')) offset++;
            if (offset < text.Length && (text[offset] == 'e' || text[offset] == 'E'))
            {
                offset++;
                if (offset < text.Length && (text[offset] == '+' || text[offset] == '-')) offset++;
                while (offset < text.Length && char.IsAsciiDigit(text[offset])) offset++;
            }
            if (!double.TryParse(text[start..offset], NumberStyles.Float, CultureInfo.InvariantCulture, out left))
                throw new JsonException("表达式包含无效数字。");
        }
        left = Finite(left);
        while (true)
        {
            SkipSpace(text, ref offset);
            if (offset >= text.Length) return left;
            // 优先级爬升保持同级运算从左到右。
            char operation = text[offset];
            int precedence = operation is '+' or '-' ? 1 : operation is '*' or '/' ? 2 : -1;
            if (precedence < minimum) return left;
            offset++;
            double right = Parse(text, ref offset, precedence + 1, depth + 1);
            left = Finite(operation switch
            {
                '+' => left + right,
                '-' => left - right,
                '*' => left * right,
                '/' when right != 0 => left / right,
                _ => throw new JsonException("表达式不能除以零。")
            });
        }
    }

    /// <summary>跳过表达式中的空白。</summary>
    /// <param name="text">表达式。</param>
    /// <param name="offset">当前字符位置。</param>
    private static void SkipSpace(string text, ref int offset)
    {
        while (offset < text.Length && char.IsWhiteSpace(text[offset])) offset++;
    }

    /// <summary>拒绝非有限数值与计算溢出。</summary>
    /// <param name="value">待验证结果。</param>
    /// <returns>原有限数值。</returns>
    private static double Finite(double value)
        => double.IsFinite(value) ? value : throw new JsonException("数值或表达式结果必须有限。");
}
