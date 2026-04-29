using EasyConvert2.Services;

namespace EasyConvert2.Tests;

public class ScaleKeyboardFactoryTests
{
    [Theory]
    [InlineData("scale_2:abc", "scale_2", "abc", 2, "2x")]
    [InlineData("scale_4:abc", "scale_4", "abc", 4, "4x")]
    [InlineData("scale_down:abc", "scale_down", "abc", 0.5, "0.5x")]
    public void TryParse_ReturnsCommand_ForValidCallbackData(
        string callbackData,
        string expectedAction,
        string expectedOperationId,
        double expectedScaleFactor,
        string expectedScaleLabel)
    {
        var factory = new ScaleKeyboardFactory();

        var result = factory.TryParse(callbackData, out var command);

        Assert.True(result);
        Assert.Equal(expectedAction, command.Action);
        Assert.Equal(expectedOperationId, command.OperationId);
        Assert.Equal(expectedScaleFactor, command.ScaleFactor);
        Assert.Equal(expectedScaleLabel, command.ScaleLabel);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("scale_2")]
    [InlineData("scale_2:")]
    [InlineData("unknown:abc")]
    public void TryParse_ReturnsFalse_ForInvalidCallbackData(string? callbackData)
    {
        var factory = new ScaleKeyboardFactory();

        var result = factory.TryParse(callbackData, out var command);

        Assert.False(result);
        Assert.True(command.IsEmpty);
    }

    [Fact]
    public void Create_BuildsKeyboard_WithOperationIdInCallbackData()
    {
        var factory = new ScaleKeyboardFactory();

        var keyboard = factory.Create("operation-id");
        var buttonRows = keyboard.InlineKeyboard.ToArray();
        var buttons = buttonRows[0].ToArray();

        Assert.Single(buttonRows);
        Assert.Equal(3, buttons.Length);
        Assert.Equal("2x", buttons[0].Text);
        Assert.Equal("scale_2:operation-id", buttons[0].CallbackData);
        Assert.Equal("4x", buttons[1].Text);
        Assert.Equal("scale_4:operation-id", buttons[1].CallbackData);
        Assert.Equal("0.5x", buttons[2].Text);
        Assert.Equal("scale_down:operation-id", buttons[2].CallbackData);
    }
}
