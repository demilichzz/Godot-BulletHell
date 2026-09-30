using System;
using System.Globalization;
using System.Text.Json;

/// <summary>仅供路径使用的参数表达式解析器；解析一次后对有限参数t重复求值。</summary>
internal sealed class VPathExpression
{
    // 表达式原文及加载阶段的字符游标，不保存任何战斗状态。
    private readonly string _text;
    private int _offset;

    /// <summary>建立独立解析状态。</summary>
    /// <param name="text">最长1024字符的表达式。</param>
    private VPathExpression(string text) => _text = text;

    /// <summary>提供仅指定t的简便求值入口，其他路径变量取0。</summary>
    /// <param name="text">受限数学表达式。</param>
    /// <returns>只接收有限参数t的纯函数。</returns>
    internal static Func<double, double> Compile(string text)
    {
        // 共用同一次解析，简便入口不在每次求值时重新解析。
        var evaluate = CompilePath(text);
        return t => evaluate(new VPathFunctionContext { T = t });
    }
    /// <summary>解析受限数学表达式，不修改通用JSON数值转换规则。</summary>
    /// <param name="text">支持t、L、SX/SY/EX/EY、PI、TAU、四则运算及白名单数学函数。</param>
    /// <returns>无随机和外部状态的有限数值求值函数。</returns>
    internal static Func<VPathFunctionContext, double> CompilePath(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 1024)
            throw new JsonException("路径表达式须为1至1024字符的非空字符串。");
        // 每次加载独立解析，委托仅捕获子表达式和常量。
        var parser = new VPathExpression(text);
        var evaluate = parser.Parse(0, 0);
        parser.SkipSpace();
        if (parser._offset != text.Length) throw new JsonException($"路径表达式位置{parser._offset}存在无效内容。");
        return context => { Finite(context.T); return Finite(evaluate(context)); };
    }

    /// <summary>按优先级构造求值委托，嵌套最多64层。</summary>
    /// <param name="minimum">最低运算优先级。</param>
    /// <param name="depth">当前嵌套深度。</param>
    /// <returns>当前子表达式求值函数。</returns>
    private Func<VPathFunctionContext, double> Parse(int minimum, int depth)
    {
        if (depth > 64) throw new JsonException("路径表达式嵌套过深。");
        SkipSpace();
        if (_offset >= _text.Length) throw new JsonException("路径表达式缺少数值。");
        // 先处理一元运算、括号、标识符或数字。
        Func<VPathFunctionContext, double> left;
        char token = _text[_offset];
        if (token is '+' or '-')
        {
            _offset++;
            var operand = Parse(3, depth + 1);
            left = token == '-' ? t => -operand(t) : operand;
        }
        else if (token == '(')
        {
            _offset++;
            left = Parse(0, depth + 1);
            Expect(')');
        }
        else if (char.IsAsciiLetter(token))
        {
            // 名称大小写严格匹配，拒绝对象访问及未列出的函数。
            int start = _offset;
            while (_offset < _text.Length && char.IsAsciiLetter(_text[_offset])) _offset++;
            string name = _text[start.._offset];
            left = name switch
            {
                "t" => context => Finite(context.T),
                "L" => context => Finite(context.L),
                "SX" => context => Finite(context.SX),
                "SY" => context => Finite(context.SY),
                "EX" => context => Finite(context.EX),
                "EY" => context => Finite(context.EY),
                "PI" => _ => Math.PI,
                "TAU" => _ => Math.Tau,
                _ => ParseFunction(name, depth + 1)
            };
        }
        else
        {
            // 固定文化的小数与科学计数法；数字在加载时检查溢出。
            int start = _offset;
            while (_offset < _text.Length && (char.IsAsciiDigit(_text[_offset]) || _text[_offset] == '.')) _offset++;
            if (_offset < _text.Length && _text[_offset] is 'e' or 'E')
            {
                _offset++;
                if (_offset < _text.Length && _text[_offset] is '+' or '-') _offset++;
                while (_offset < _text.Length && char.IsAsciiDigit(_text[_offset])) _offset++;
            }
            if (!double.TryParse(_text[start.._offset], NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                throw new JsonException("路径表达式包含无效数字。");
            number = Finite(number);
            left = _ => number;
        }
        while (true)
        {
            SkipSpace();
            if (_offset >= _text.Length) return left;
            // 同级运算保持从左到右，捕获本轮委托而非可变left。
            char operation = _text[_offset];
            int precedence = operation is '+' or '-' ? 1 : operation is '*' or '/' ? 2 : -1;
            if (precedence < minimum) return left;
            _offset++;
            var previous = left;
            var right = Parse(precedence + 1, depth + 1);
            left = t => Calculate(operation, previous(t), right(t));
        }
    }

    /// <summary>解析固定参数数量的白名单函数。</summary>
    /// <param name="name">区分大小写的函数名称。</param>
    /// <param name="depth">当前嵌套深度。</param>
    /// <returns>检查定义域和溢出的求值函数。</returns>
    private Func<VPathFunctionContext, double> ParseFunction(string name, int depth)
    {
        if (name is not ("sin" or "cos" or "tan" or "sqrt" or "abs" or "pow"))
            throw new JsonException("不支持的路径函数或变量：" + name);
        Expect('(');
        // pow有两个参数，其余函数只有一个。
        var first = Parse(0, depth);
        if (name == "pow")
        {
            Expect(',');
            var second = Parse(0, depth);
            Expect(')');
            return t => Finite(Math.Pow(first(t), second(t)));
        }
        Expect(')');
        return t => Finite(name switch
        {
            "sin" => Math.Sin(first(t)),
            "cos" => Math.Cos(first(t)),
            "tan" => Math.Tan(first(t)),
            "sqrt" => Math.Sqrt(first(t)),
            _ => Math.Abs(first(t))
        });
    }

    /// <summary>执行一次四则运算并拒绝除零及非有限结果。</summary>
    /// <param name="operation">四则运算符。</param>
    /// <param name="left">有限左操作数。</param>
    /// <param name="right">有限右操作数。</param>
    /// <returns>有限运算结果。</returns>
    private static double Calculate(char operation, double left, double right)
        => Finite(operation switch
        {
            '+' => left + right,
            '-' => left - right,
            '*' => left * right,
            '/' when right != 0 => left / right,
            _ => throw new JsonException("路径表达式不能除以零。")
        });

    /// <summary>消费必须出现的分隔符。</summary>
    /// <param name="token">预期的括号或逗号。</param>
    private void Expect(char token)
    {
        SkipSpace();
        if (_offset >= _text.Length || _text[_offset++] != token)
            throw new JsonException($"路径表达式缺少{token}。");
    }

    /// <summary>跳过当前游标后的空白。</summary>
    private void SkipSpace()
    {
        while (_offset < _text.Length && char.IsWhiteSpace(_text[_offset])) _offset++;
    }

    /// <summary>拒绝非法数学定义域及溢出。</summary>
    /// <param name="value">当前输入或运算结果。</param>
    /// <returns>原有限值。</returns>
    private static double Finite(double value)
        => double.IsFinite(value) ? value : throw new JsonException("路径表达式结果必须有限且满足函数定义域。");
}

/// <summary>一段函数路径在某次批次求值时的只读参数，不保存到全局解析器。</summary>
internal readonly record struct VPathFunctionContext
{
    /// <summary>实际参数值，范围由TMin/TMax配置决定。</summary>
    public double T { get; init; }
    /// <summary>起终点直线距离，单位为逻辑像素。</summary>
    public double L { get; init; }
    /// <summary>起点世界横坐标，单位为逻辑像素。</summary>
    public double SX { get; init; }
    /// <summary>起点世界纵坐标，单位为逻辑像素。</summary>
    public double SY { get; init; }
    /// <summary>终点世界横坐标，单位为逻辑像素。</summary>
    public double EX { get; init; }
    /// <summary>终点世界纵坐标，单位为逻辑像素。</summary>
    public double EY { get; init; }
}