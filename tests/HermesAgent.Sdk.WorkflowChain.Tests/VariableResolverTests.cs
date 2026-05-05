using Xunit;

namespace HermesAgent.Sdk.WorkflowChain.Tests;

public class VariableResolverTests
{
    private readonly WorkflowContext _context = new();

    // ═══════════════════════════════════════════
    // 路径解析 - 步骤输出
    // ═══════════════════════════════════════════

    [Fact]
    public void Resolve_StepOutput_String()
    {
        // Arrange
        _context.StepOutputs["step-1"] = "hello";
        var resolver = new VariableResolver(_context);

        // Act
        var result = resolver.Resolve("{{steps.step-1.output}}");

        // Assert
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Resolve_StepOutput_Number()
    {
        // Arrange
        _context.StepOutputs["step-1"] = 42;
        var resolver = new VariableResolver(_context);

        // Act
        var result = resolver.Resolve("{{steps.step-1.output}}");

        // Assert
        Assert.Equal("42", result);
    }

    [Fact]
    public void Resolve_StepOutput_InitialInput()
    {
        // Arrange — InitialInput can be used via context
        var ctx = new WorkflowContext { InitialInput = { ["key1"] = "value1" } };
        var resolver = new VariableResolver(ctx);

        // Act — access via context.data_key (if set in Data)
        ctx.Data["input_key"] = ctx.InitialInput["key1"];
        var result = resolver.Resolve("{{context.input_key}}");

        // Assert
        Assert.Equal("value1", result);
    }

    // ═══════════════════════════════════════════
    // 路径解析 - 上下文数据
    // ═══════════════════════════════════════════

    [Fact]
    public void Resolve_ContextData_String()
    {
        // Arrange
        _context.Data["name"] = "Hermes";
        var resolver = new VariableResolver(_context);

        // Act
        var result = resolver.Resolve("{{context.name}}");

        // Assert
        Assert.Equal("Hermes", result);
    }

    [Fact]
    public void Resolve_ContextData_Number()
    {
        // Arrange
        _context.Data["count"] = 99;
        var resolver = new VariableResolver(_context);

        // Act
        var result = resolver.Resolve("{{context.count}}");

        // Assert
        Assert.Equal("99", result);
    }

    // ═══════════════════════════════════════════
    // 嵌套属性
    // ═══════════════════════════════════════════

    [Fact]
    public void Resolve_NestedProperty_Dictionary()
    {
        // Arrange
        var output = new Dictionary<string, object?>
        {
            ["user"] = new Dictionary<string, object?>
            {
                ["name"] = "Alice",
                ["age"] = 30
            }
        };
        _context.StepOutputs["step-1"] = output;
        var resolver = new VariableResolver(_context);

        // Act
        var result = resolver.Resolve("{{steps.step-1.output.user.name}}");

        // Assert
        Assert.Equal("Alice", result);
    }

    [Fact]
    public void Resolve_NestedProperty_Reflection()
    {
        // Arrange
        var output = new TestPoco { Name = "Bob", Value = 123 };
        _context.StepOutputs["step-1"] = output;
        var resolver = new VariableResolver(_context);

        // Act
        var result = resolver.Resolve("{{steps.step-1.output.Name}}");

        // Assert
        Assert.Equal("Bob", result);
    }

    [Fact]
    public void Resolve_DeepNesting_ThreeLevels()
    {
        // Arrange
        var output = new Dictionary<string, object?>
        {
            ["level1"] = new Dictionary<string, object?>
            {
                ["level2"] = new Dictionary<string, object?>
                {
                    ["level3"] = "deep_value"
                }
            }
        };
        _context.StepOutputs["step-1"] = output;
        var resolver = new VariableResolver(_context);

        // Act
        var result = resolver.Resolve("{{steps.step-1.output.level1.level2.level3}}");

        // Assert
        Assert.Equal("deep_value", result);
    }

    // ═══════════════════════════════════════════
    // 边界情况
    // ═══════════════════════════════════════════

    [Fact]
    public void Resolve_NullValue_ReturnsNullString()
    {
        // Arrange
        _context.StepOutputs["step-1"] = null;
        var resolver = new VariableResolver(_context);

        // Act
        var result = resolver.Resolve("{{steps.step-1.output}}");

        // Assert
        Assert.Equal("null", result);
    }

    [Fact]
    public void Resolve_MissingStep_ThrowsInvalidOperationException()
    {
        // Arrange
        var resolver = new VariableResolver(_context);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            resolver.Resolve("{{steps.nonexistent.output}}"));
    }

    [Fact]
    public void Resolve_MissingContextKey_ThrowsInvalidOperationException()
    {
        // Arrange
        var resolver = new VariableResolver(_context);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            resolver.Resolve("{{context.missing_key}}"));
    }

    [Fact]
    public void Resolve_EmptyTemplate_ReturnsEmpty()
    {
        // Arrange
        var resolver = new VariableResolver(_context);

        // Act
        var result = resolver.Resolve("");

        // Assert
        Assert.Equal("", result);
    }

    [Fact]
    public void Resolve_NullTemplate_ReturnsNull()
    {
        // Arrange
        var resolver = new VariableResolver(_context);

        // Act
        var result = resolver.Resolve(null!);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Resolve_UnknownExpression_ThrowsInvalidOperationException()
    {
        // Arrange
        var resolver = new VariableResolver(_context);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            resolver.Resolve("{{unknown.expression}}"));
    }

    // ═══════════════════════════════════════════
    // 模板替换
    // ═══════════════════════════════════════════

    [Fact]
    public void Resolve_TemplateWithMultipleVariables()
    {
        // Arrange
        _context.StepOutputs["greet"] = "Hello";
        _context.Data["target"] = "World";
        var resolver = new VariableResolver(_context);

        // Act
        var result = resolver.Resolve("{{steps.greet.output}}, {{context.target}}!");

        // Assert
        Assert.Equal("Hello, World!", result);
    }

    [Fact]
    public void Resolve_LiteralTextOnly_ReturnsLiteral()
    {
        // Arrange
        var resolver = new VariableResolver(_context);

        // Act
        var result = resolver.Resolve("just plain text");

        // Assert
        Assert.Equal("just plain text", result);
    }

    [Fact]
    public void Resolve_BooleanValue_Serialized()
    {
        // Arrange
        _context.Data["active"] = true;
        var resolver = new VariableResolver(_context);

        // Act
        var result = resolver.Resolve("{{context.active}}");

        // Assert
        Assert.Equal("True", result);
    }

    // Helper POCO for reflection tests
    private class TestPoco
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }
}
