using System;

namespace JxTerminal.Controls;

/// <summary>
/// Provides the necessary interface for TerminalControl to render content and handle input.
/// Implement this to drive the TerminalControl.
/// </summary>
public interface ITerminalController
{
    /// <summary>
    /// The XTerm terminal state (buffers, cursor, options).
    /// The Control reads from this to render.
    /// </summary>
    XTerm.Terminal Terminal { get; }

    /// <summary>
    /// Event fired when the terminal content has changed and the screen needs repainting.
    /// </summary>
    event Action? ScreenUpdated;

    /// <summary>
    /// Sends user input (keyboard/mouse) to the backend session.
    /// </summary>
    void SendInput(string input);

    /// <summary>
    /// Notifies the backend that the terminal view has been resized.
    /// </summary>
    void Resize(int rows, int cols, int width, int height);

    /// <summary>True when the remote application requested xterm mouse reporting.</summary>
    bool MouseTracking { get; }

    /// <summary>True when all motion events should be reported (mode 1003), not just drags.</summary>
    bool MouseAnyEvent { get; }

    /// <summary>True when mouse events should be encoded with SGR (CSI &lt; b ; x ; y M/m).</summary>
    bool SgrMouse { get; }

    /// <summary>True when DECCKM is set and arrows must use SS3 (ESC O A) forms.</summary>
    bool ApplicationCursorKeys { get; }

    /// <summary>True when pasted text must be wrapped in bracketed-paste markers.</summary>
    bool BracketedPaste { get; }
}
