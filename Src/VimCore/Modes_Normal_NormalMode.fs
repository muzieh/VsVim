﻿#light

namespace Vim.Modes.Normal
open Vim
open Vim.Modes
open Microsoft.VisualStudio.Text
open Microsoft.VisualStudio.Text.Editor

type internal NormalModeData = {
    Command : string
    InReplace : bool
}

type internal NormalMode 
    ( 
        _vimBufferData : IVimBufferData,
        _operations : ICommonOperations,
        _motionUtil : IMotionUtil,
        _runner : ICommandRunner,
        _capture : IMotionCapture
    ) as this =

    let _vimTextBuffer = _vimBufferData.VimTextBuffer
    let _textView = _vimBufferData.TextView
    let _localSettings = _vimTextBuffer.LocalSettings
    let _globalSettings = _vimTextBuffer.GlobalSettings
    let _statusUtil = _vimBufferData.StatusUtil

    /// Reset state for data in Normal Mode
    static let EmptyData = {
        Command = StringUtil.empty
        InReplace = false
    }

    /// Set of all char's Vim is interested in 
    let _coreCharSet = KeyInputUtil.VimKeyCharList |> Set.ofList

    /// Contains the state information for Normal mode
    let mutable _data = EmptyData

    /// This is the list of commands that have the same key binding as the selection 
    /// commands and run when keymodel doesn't have startsel as a value
    let mutable _selectionAlternateCommands : CommandBinding list = List.empty

    let _eventHandlers = DisposableBag()

    /// The set of standard commands that are shared amongst all instances 
    static let SharedStandardCommands =
        let normalSeq = 
            seq {
                yield ("a", CommandFlags.LinkedWithNextCommand ||| CommandFlags.Repeatable, NormalCommand.InsertAfterCaret)
                yield ("A", CommandFlags.LinkedWithNextCommand ||| CommandFlags.Repeatable, NormalCommand.InsertAtEndOfLine)
                yield ("C", CommandFlags.LinkedWithNextCommand ||| CommandFlags.Repeatable, NormalCommand.ChangeTillEndOfLine)
                yield ("cc", CommandFlags.LinkedWithNextCommand ||| CommandFlags.Repeatable, NormalCommand.ChangeLines)
                yield ("dd", CommandFlags.Repeatable, NormalCommand.DeleteLines)
                yield ("D", CommandFlags.Repeatable, NormalCommand.DeleteTillEndOfLine)
                yield ("gf", CommandFlags.None, NormalCommand.GoToFileUnderCaret false)
                yield ("gJ", CommandFlags.Repeatable, NormalCommand.JoinLines JoinKind.KeepEmptySpaces)
                yield ("gh", CommandFlags.Special, NormalCommand.SwitchMode (ModeKind.SelectCharacter, ModeArgument.None))
                yield ("gH", CommandFlags.Special, NormalCommand.SwitchMode (ModeKind.SelectLine, ModeArgument.None))
                yield ("g<C-h>", CommandFlags.Special, NormalCommand.SwitchMode (ModeKind.SelectBlock, ModeArgument.None))
                yield ("gI", CommandFlags.None, NormalCommand.InsertAtStartOfLine)
                yield ("gp", CommandFlags.Repeatable, NormalCommand.PutAfterCaret true)
                yield ("gP", CommandFlags.Repeatable, NormalCommand.PutBeforeCaret true)
                yield ("gt", CommandFlags.Special, NormalCommand.GoToNextTab Path.Forward)
                yield ("gT", CommandFlags.Special, NormalCommand.GoToNextTab Path.Backward)
                yield ("gv", CommandFlags.Special, NormalCommand.SwitchPreviousVisualMode)
                yield ("gugu", CommandFlags.Repeatable, NormalCommand.ChangeCaseCaretLine ChangeCharacterKind.ToLowerCase)
                yield ("guu", CommandFlags.Repeatable, NormalCommand.ChangeCaseCaretLine ChangeCharacterKind.ToLowerCase)
                yield ("gUgU", CommandFlags.Repeatable, NormalCommand.ChangeCaseCaretLine ChangeCharacterKind.ToUpperCase)
                yield ("gUU", CommandFlags.Repeatable, NormalCommand.ChangeCaseCaretLine ChangeCharacterKind.ToUpperCase)
                yield ("g~g~", CommandFlags.Repeatable, NormalCommand.ChangeCaseCaretLine ChangeCharacterKind.ToggleCase)
                yield ("g~~", CommandFlags.Repeatable, NormalCommand.ChangeCaseCaretLine ChangeCharacterKind.ToggleCase)
                yield ("g?g?", CommandFlags.Repeatable, NormalCommand.ChangeCaseCaretLine ChangeCharacterKind.Rot13)
                yield ("g??", CommandFlags.Repeatable, NormalCommand.ChangeCaseCaretLine ChangeCharacterKind.Rot13)
                yield ("g&", CommandFlags.Special, NormalCommand.RepeatLastSubstitute true)
                yield ("i", CommandFlags.None, NormalCommand.InsertBeforeCaret)
                yield ("I", CommandFlags.LinkedWithNextCommand ||| CommandFlags.Repeatable, NormalCommand.InsertAtFirstNonBlank)
                yield ("J", CommandFlags.Repeatable, NormalCommand.JoinLines JoinKind.RemoveEmptySpaces)
                yield ("o", CommandFlags.LinkedWithNextCommand ||| CommandFlags.Repeatable, NormalCommand.InsertLineBelow)
                yield ("O", CommandFlags.LinkedWithNextCommand ||| CommandFlags.Repeatable, NormalCommand.InsertLineAbove)
                yield ("p", CommandFlags.Repeatable, NormalCommand.PutAfterCaret false)
                yield ("P", CommandFlags.Repeatable, NormalCommand.PutBeforeCaret false)
                yield ("R", CommandFlags.Repeatable ||| CommandFlags.LinkedWithNextCommand, NormalCommand.ReplaceAtCaret)
                yield ("s", CommandFlags.LinkedWithNextCommand ||| CommandFlags.Repeatable, NormalCommand.SubstituteCharacterAtCaret)
                yield ("S", CommandFlags.LinkedWithNextCommand ||| CommandFlags.Repeatable, NormalCommand.ChangeLines)
                yield ("u", CommandFlags.Special, NormalCommand.Undo)
                yield ("v", CommandFlags.Special, NormalCommand.SwitchMode (ModeKind.VisualCharacter, ModeArgument.None))
                yield ("V", CommandFlags.Special, NormalCommand.SwitchMode (ModeKind.VisualLine, ModeArgument.None))
                yield ("x", CommandFlags.Repeatable, NormalCommand.DeleteCharacterAtCaret)
                yield ("X", CommandFlags.Repeatable, NormalCommand.DeleteCharacterBeforeCaret)
                yield ("Y", CommandFlags.None, NormalCommand.YankLines)
                yield ("yy", CommandFlags.None, NormalCommand.YankLines)
                yield ("za", CommandFlags.Special, NormalCommand.ToggleFoldUnderCaret)
                yield ("zA", CommandFlags.Special, NormalCommand.ToggleAllFolds)
                yield ("zo", CommandFlags.Special, NormalCommand.OpenFoldUnderCaret)
                yield ("zO", CommandFlags.Special, NormalCommand.OpenAllFoldsUnderCaret)
                yield ("zc", CommandFlags.Special, NormalCommand.CloseFoldUnderCaret)
                yield ("zC", CommandFlags.Special, NormalCommand.CloseAllFoldsUnderCaret)
                yield ("zd", CommandFlags.Special, NormalCommand.DeleteFoldUnderCaret)
                yield ("zD", CommandFlags.Special, NormalCommand.DeleteAllFoldsUnderCaret)
                yield ("zE", CommandFlags.Special, NormalCommand.DeleteAllFoldsInBuffer)
                yield ("zF", CommandFlags.Special, NormalCommand.FoldLines)
                yield ("zM", CommandFlags.Special, NormalCommand.CloseAllFolds)
                yield ("zR", CommandFlags.Special, NormalCommand.OpenAllFolds)
                yield ("ZQ", CommandFlags.Special, NormalCommand.CloseBuffer)
                yield ("ZZ", CommandFlags.Special, NormalCommand.WriteBufferAndQuit)
                yield ("<Insert>", CommandFlags.None, NormalCommand.InsertBeforeCaret)
                yield ("<C-a>", CommandFlags.Repeatable, NormalCommand.AddToWord)
                yield ("<C-i>", CommandFlags.Movement, NormalCommand.JumpToNewerPosition)
                yield ("<C-o>", CommandFlags.Movement, NormalCommand.JumpToOlderPosition)
                yield ("<C-PageDown>", CommandFlags.Special, NormalCommand.GoToNextTab Path.Forward)
                yield ("<C-PageUp>", CommandFlags.Special, NormalCommand.GoToNextTab Path.Backward)
                yield ("<C-q>", CommandFlags.Special, NormalCommand.SwitchMode (ModeKind.VisualBlock, ModeArgument.None))
                yield ("<C-r>", CommandFlags.Special, NormalCommand.Redo)
                yield ("<C-v>", CommandFlags.Special, NormalCommand.SwitchMode (ModeKind.VisualBlock, ModeArgument.None))
                yield ("<C-w><C-j>", CommandFlags.None, NormalCommand.GoToView Direction.Down)
                yield ("<C-w>j", CommandFlags.None, NormalCommand.GoToView Direction.Down)
                yield ("<C-w><C-k>", CommandFlags.None, NormalCommand.GoToView Direction.Up)
                yield ("<C-w>k", CommandFlags.None, NormalCommand.GoToView Direction.Up)
                yield ("<C-w><C-l>", CommandFlags.None, NormalCommand.GoToView Direction.Right)
                yield ("<C-w>l", CommandFlags.None, NormalCommand.GoToView Direction.Right)
                yield ("<C-w><C-h>", CommandFlags.None, NormalCommand.GoToView Direction.Left)
                yield ("<C-w>h", CommandFlags.None, NormalCommand.GoToView Direction.Left)
                yield ("<C-w><C-s>", CommandFlags.None, NormalCommand.SplitViewHorizontally)
                yield ("<C-w>s", CommandFlags.None, NormalCommand.SplitViewHorizontally)
                yield ("<C-w><C-v>", CommandFlags.None, NormalCommand.SplitViewVertically)
                yield ("<C-w>v", CommandFlags.None, NormalCommand.SplitViewVertically)
                yield ("<C-w><C-g><C-f>", CommandFlags.None, NormalCommand.GoToFileUnderCaret true)
                yield ("<C-w>gf", CommandFlags.None, NormalCommand.GoToFileUnderCaret true)
                yield ("<C-x>", CommandFlags.Repeatable, NormalCommand.SubtractFromWord)
                yield ("<C-]>", CommandFlags.Special, NormalCommand.GoToDefinition)
                yield ("<Del>", CommandFlags.Repeatable, NormalCommand.DeleteCharacterAtCaret)
                yield ("[p", CommandFlags.Repeatable, NormalCommand.PutBeforeCaretWithIndent)
                yield ("[P", CommandFlags.Repeatable, NormalCommand.PutBeforeCaretWithIndent)
                yield ("]p", CommandFlags.Repeatable, NormalCommand.PutAfterCaretWithIndent)
                yield ("]P", CommandFlags.Repeatable, NormalCommand.PutBeforeCaretWithIndent)
                yield ("&", CommandFlags.Special, NormalCommand.RepeatLastSubstitute false)
                yield (".", CommandFlags.Special, NormalCommand.RepeatLastCommand)
                yield ("<lt><lt>", CommandFlags.Repeatable, NormalCommand.ShiftLinesLeft)
                yield (">>", CommandFlags.Repeatable, NormalCommand.ShiftLinesRight)
                yield ("==", CommandFlags.Repeatable, NormalCommand.FormatLines)
                yield (":", CommandFlags.Special, NormalCommand.SwitchMode (ModeKind.Command, ModeArgument.None))
            } |> Seq.map (fun (str, flags, command) -> 
                let keyInputSet = KeyNotationUtil.StringToKeyInputSet str
                CommandBinding.NormalBinding (keyInputSet, flags, command))
            
        let motionSeq = 
            seq {
                yield ("c", CommandFlags.Change ||| CommandFlags.LinkedWithNextCommand ||| CommandFlags.Repeatable, NormalCommand.ChangeMotion)
                yield ("d", CommandFlags.Repeatable ||| CommandFlags.Delete, NormalCommand.DeleteMotion)
                yield ("gU", CommandFlags.Repeatable, (fun motion -> NormalCommand.ChangeCaseMotion (ChangeCharacterKind.ToUpperCase, motion)))
                yield ("gu", CommandFlags.Repeatable, (fun motion -> NormalCommand.ChangeCaseMotion (ChangeCharacterKind.ToLowerCase, motion)))
                yield ("g?", CommandFlags.Repeatable, (fun motion -> NormalCommand.ChangeCaseMotion (ChangeCharacterKind.Rot13, motion)))
                yield ("y", CommandFlags.Yank, NormalCommand.Yank)
                yield ("zf", CommandFlags.None, NormalCommand.FoldMotion)
                yield ("<lt>", CommandFlags.Repeatable ||| CommandFlags.ShiftLeft, NormalCommand.ShiftMotionLinesLeft)
                yield (">", CommandFlags.Repeatable ||| CommandFlags.ShiftRight, NormalCommand.ShiftMotionLinesRight)
                yield ("=", CommandFlags.Repeatable, NormalCommand.FormatMotion)
            } |> Seq.map (fun (str, flags, command) -> 
                let keyInputSet = KeyNotationUtil.StringToKeyInputSet str
                CommandBinding.MotionBinding (keyInputSet, flags, command))

        Seq.append normalSeq motionSeq 
        |> List.ofSeq

    /// The set of selection starting commands.  These are only enabled when 'keymodel' contains
    /// the startsel option
    static let SharedSelectionCommands =
        seq {
            yield ("<S-Up>", CommandFlags.Repeatable, NormalCommand.SwitchToSelection CaretMovement.Up)
            yield ("<S-Right>", CommandFlags.Repeatable, NormalCommand.SwitchToSelection CaretMovement.Right)
            yield ("<S-Down>", CommandFlags.Repeatable, NormalCommand.SwitchToSelection CaretMovement.Down)
            yield ("<S-Left>", CommandFlags.Repeatable, NormalCommand.SwitchToSelection CaretMovement.Left)
            yield ("<S-Home>", CommandFlags.Repeatable, NormalCommand.SwitchToSelection CaretMovement.Home)
            yield ("<S-End>", CommandFlags.Repeatable, NormalCommand.SwitchToSelection CaretMovement.End)
            yield ("<S-PageUp>", CommandFlags.Repeatable, NormalCommand.SwitchToSelection CaretMovement.PageUp)
            yield ("<S-PageDown>", CommandFlags.Repeatable, NormalCommand.SwitchToSelection CaretMovement.PageDown)
        } 
        |> Seq.map (fun (str, flags, command) -> 
            let keyInputSet = KeyNotationUtil.StringToKeyInputSet str
            CommandBinding.NormalBinding (keyInputSet, flags, command))
        |> List.ofSeq

    static let SharedSelectionCommandNameSet = 
        SharedSelectionCommands
        |> Seq.map (fun binding -> binding.KeyInputSet)
        |> Set.ofSeq

    do
        // Up cast here to work around the F# bug which prevents accessing a CLIEvent from
        // a derived type
        let settings = _globalSettings :> IVimSettings
        settings.SettingChanged.Subscribe this.OnGlobalSettingsChanged |> _eventHandlers.Add

    member x.TextView = _vimBufferData.TextView
    member x.TextBuffer = _vimTextBuffer.TextBuffer
    member x.CaretPoint = this.TextView.Caret.Position.BufferPosition
    member x.IsCommandRunnerPopulated = _runner.Commands |> SeqUtil.isNotEmpty
    member x.KeyRemapMode = 
        if _runner.IsWaitingForMoreInput then
            _runner.KeyRemapMode
        else
            Some KeyRemapMode.Normal
    member x.Command = _data.Command
    member x.Commands = 
        this.EnsureCommands()
        _runner.Commands
    member x.CommandNames = 
        x.EnsureCommands()
        _runner.Commands |> Seq.map (fun command -> command.KeyInputSet)

    member x.EnsureCommands() = 
        if not x.IsCommandRunnerPopulated then
            let factory = CommandFactory(_operations, _capture, _motionUtil, _vimBufferData.JumpList, _localSettings)

            let complexSeq = 
                seq {
                    yield ("r", CommandFlags.Repeatable, x.BindReplaceChar ())
                    yield ("'", CommandFlags.Movement, x.BindMark NormalCommand.JumpToMarkLine)
                    yield ("`", CommandFlags.Movement, x.BindMark NormalCommand.JumpToMark)
                    yield ("m", CommandFlags.Special, BindDataStorage<_>.CreateForSingleChar None NormalCommand.SetMarkToCaret)
                    yield ("@", CommandFlags.Special, BindDataStorage<_>.CreateForSingleChar None NormalCommand.RunMacro)
                } |> Seq.map (fun (str, flags, storage) -> 
                    let keyInputSet = KeyNotationUtil.StringToKeyInputSet str
                    CommandBinding.ComplexNormalBinding (keyInputSet, flags, storage))

            SharedStandardCommands
            |> Seq.append complexSeq
            |> Seq.append (factory.CreateMovementCommands())
            |> Seq.append (factory.CreateScrollCommands())
            |> Seq.iter _runner.Add

            // Add in the special ~ command
            let _, command = x.GetTildeCommand()
            _runner.Add command

            // Add in the macro command
            factory.CreateMacroEditCommands _runner _vimTextBuffer.Vim.MacroRecorder _eventHandlers

            // Save the alternate selection command bindings
            _selectionAlternateCommands <-
                _runner.Commands
                |> Seq.filter (fun commandBinding -> Set.contains commandBinding.KeyInputSet SharedSelectionCommandNameSet)
                |> List.ofSeq

            // The command list is built without selection commands.  If selection is enabled go 
            // ahead and switch over now 
            if Util.IsFlagSet _globalSettings.KeyModelOptions KeyModelOptions.StartSelection then
                x.UpdateSelectionCommands()

    /// Raised when a global setting is changed
    member x.OnGlobalSettingsChanged (args : SettingEventArgs) = 
        if x.IsCommandRunnerPopulated then
            let setting = args.Setting
            if StringUtil.isEqual setting.Name GlobalSettingNames.TildeOpName then
                x.UpdateTildeCommand()
            elif StringUtil.isEqual setting.Name GlobalSettingNames.KeyModelName then
                x.UpdateSelectionCommands()

    /// Bind the character in a replace character command: 'r'.  
    member x.BindReplaceChar () =
        let func () = 
            _data <- { _data with InReplace = true }

            let bind (keyInput : KeyInput) = 
                _data <- { _data with InReplace = false }
                match keyInput.Key with
                | VimKey.Escape -> BindResult.Cancelled
                | VimKey.Back -> BindResult.Cancelled
                | VimKey.Delete -> BindResult.Cancelled
                | _ -> NormalCommand.ReplaceChar keyInput |> BindResult.Complete

            {
                KeyRemapMode = Some KeyRemapMode.Language
                BindFunction = bind }
        BindDataStorage.Complex func

    /// Get a mark and us the provided 'func' to create a Motion value
    member x.BindMark func = 
        let bindFunc (keyInput : KeyInput) =
            match Mark.OfChar keyInput.Char with
            | None -> BindResult<NormalCommand>.Error
            | Some localMark -> BindResult<_>.Complete (func localMark)
        let bindData = {
            KeyRemapMode = None
            BindFunction = bindFunc }
        BindDataStorage<_>.Simple bindData

    /// Get the information on how to handle the tilde command based on the current setting for 'tildeop'
    member x.GetTildeCommand() =
        let name = KeyInputUtil.CharToKeyInput '~' |> KeyInputSet.OneKeyInput
        let flags = CommandFlags.Repeatable
        let command = 
            if _globalSettings.TildeOp then
                CommandBinding.MotionBinding (name, flags, (fun motion -> NormalCommand.ChangeCaseMotion (ChangeCharacterKind.ToggleCase, motion)))
            else
                CommandBinding.NormalBinding (name, flags, NormalCommand.ChangeCaseCaretPoint ChangeCharacterKind.ToggleCase)
        name, command

    /// Ensure that the correct commands are loaded for the selection KeyInput values 
    member x.UpdateSelectionCommands() =

        // Remove all of the commands with that binding
        SharedSelectionCommandNameSet |> Seq.iter _runner.Remove

        let commandList = 
            if Util.IsFlagSet _globalSettings.KeyModelOptions KeyModelOptions.StartSelection then
                SharedSelectionCommands
            else
                _selectionAlternateCommands
        commandList |> List.iter _runner.Add

    member x.UpdateTildeCommand() =
        let name, command = x.GetTildeCommand()
        _runner.Remove name
        _runner.Add command

    /// Create the CommandBinding instances for the supported NormalCommand values
    member x.Reset() =
        _runner.ResetState()
        _data <- EmptyData

    member x.CanProcess (keyInput : KeyInput) =
        let doesCommandStartWith keyInput =
            let name = KeyInputSet.OneKeyInput keyInput
            _runner.Commands 
            |> Seq.filter (fun command -> command.KeyInputSet.StartsWith name)
            |> SeqUtil.isNotEmpty

        if _runner.IsWaitingForMoreInput then 
            true
        elif doesCommandStartWith keyInput then 
            true
        elif Option.isSome keyInput.RawChar && KeyModifiers.None = keyInput.KeyModifiers then
            // We can process any letter (think international input) or any character
            // which is part of the standard Vim input set
            CharUtil.IsLetter keyInput.Char || Set.contains keyInput.Char _coreCharSet
        else 
            false
    
    member x.Process (keyInput : KeyInput) = 
        let command = _data.Command + keyInput.Char.ToString()
        _data <- { _data with Command = command }

        match _runner.Run keyInput with
        | BindResult.NeedMoreInput _ -> 
            ProcessResult.HandledNeedMoreInput
        | BindResult.Complete commandData -> 
            x.Reset()
            ProcessResult.OfCommandResult commandData.CommandResult
        | BindResult.Error -> 
            x.Reset()
            ProcessResult.Handled ModeSwitch.NoSwitch
        | BindResult.Cancelled -> 
            x.Reset()
            ProcessResult.Handled ModeSwitch.NoSwitch

    member x.OnEnter arg =
        x.EnsureCommands()
        x.Reset()

        // Process the argument if it's applicable
        match arg with 
        | ModeArgument.None -> ()
        | ModeArgument.FromVisual -> ()
        | ModeArgument.Substitute(_) -> ()
        | ModeArgument.InitialVisualSelection _ -> ()
        | ModeArgument.InsertBlock (_, transaction) -> transaction.Complete()
        | ModeArgument.InsertWithCount _ -> ()
        | ModeArgument.InsertWithCountAndNewLine _ -> ()
        | ModeArgument.InsertWithTransaction transaction -> transaction.Complete()

    interface INormalMode with 
        member x.KeyRemapMode = x.KeyRemapMode
        member x.InCount = _runner.InCount
        member x.InReplace = _data.InReplace
        member x.VimTextBuffer = _vimTextBuffer
        member x.Command = x.Command
        member x.CommandRunner = _runner
        member x.CommandNames = x.CommandNames
        member x.ModeKind = ModeKind.Normal
        member x.CanProcess keyInput = x.CanProcess keyInput
        member x.Process keyInput = x.Process keyInput
        member x.OnEnter arg = x.OnEnter arg
        member x.OnLeave () = ()
        member x.OnClose() = _eventHandlers.DisposeAll()
    

