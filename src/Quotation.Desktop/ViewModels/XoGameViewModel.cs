using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Quotation.Desktop.ViewModels;

/// <summary>One box of the XO board.</summary>
public sealed partial class XoCell(int index) : ObservableObject
{
    public int Index { get; } = index;
    /// <summary>"X", "O" or "" (empty).</summary>
    [ObservableProperty] private string _mark = "";
    /// <summary>One of the three boxes in a row that won.</summary>
    [ObservableProperty] private bool _isWin;
    public bool IsX => Mark == "X";
    public bool IsO => Mark == "O";
    partial void OnMarkChanged(string value)
    {
        OnPropertyChanged(nameof(IsX));
        OnPropertyChanged(nameof(IsO));
    }
}

/// <summary>
/// The hidden XO game (Ctrl+Alt+Shift+X): you are X, the computer is O. The computer plays well but not perfectly, so it can
/// be beaten. Nothing is saved; the score lasts while the app is open.
/// </summary>
public sealed partial class XoGameViewModel : ObservableObject
{
    private static readonly int[][] Rows =
    [
        [0, 1, 2], [3, 4, 5], [6, 7, 8], [0, 3, 6], [1, 4, 7], [2, 5, 8], [0, 4, 8], [2, 4, 6],
    ];

    private readonly Random _random;
    /// <summary>How often the computer plays its best move (otherwise any free box); winning and blocking come first.</summary>
    public double Skill { get; set; } = 0.75;

    public ObservableCollection<XoCell> Cells { get; } = new(Enumerable.Range(0, 9).Select(i => new XoCell(i)));

    [ObservableProperty] private int _youWins;
    [ObservableProperty] private int _draws;
    [ObservableProperty] private int _computerWins;
    [ObservableProperty] private string _status = "Your turn (X)";
    [ObservableProperty] private bool _isOver;

    public XoGameViewModel(Random? random = null) => _random = random ?? new Random();

    [RelayCommand]
    private void Play(XoCell? cell)
    {
        if (cell is null || IsOver || cell.Mark.Length > 0) return;
        cell.Mark = "X";
        if (Finish()) return;
        var move = ChooseMove();
        Cells[move].Mark = "O";
        Finish();
    }

    [RelayCommand]
    private void NewGame()
    {
        foreach (var c in Cells)
        {
            c.Mark = "";
            c.IsWin = false;
        }
        IsOver = false;
        Status = "Your turn (X)";
    }

    [RelayCommand]
    private void ResetScore()
    {
        YouWins = Draws = ComputerWins = 0;
        NewGame();
    }

    /// <summary>True when the game is over (a row, or a full board).</summary>
    private bool Finish()
    {
        var board = Cells.Select(c => c.Mark).ToArray();
        if (Winner(board) is { } row)
        {
            foreach (var i in row) Cells[i].IsWin = true;
            IsOver = true;
            if (board[row[0]] == "X")
            {
                YouWins++;
                Status = "You win! 🎉";
            }
            else
            {
                ComputerWins++;
                Status = "The computer wins";
            }
            return true;
        }
        if (board.All(m => m.Length > 0))
        {
            Draws++;
            IsOver = true;
            Status = "Draw";
            return true;
        }
        Status = "Your turn (X)";
        return false;
    }

    private static int[]? Winner(string[] b) =>
        Rows.FirstOrDefault(r => b[r[0]].Length > 0 && b[r[0]] == b[r[1]] && b[r[1]] == b[r[2]]);

    private int ChooseMove()
    {
        var board = Cells.Select(c => c.Mark).ToArray();
        var free = Enumerable.Range(0, 9).Where(i => board[i].Length == 0).ToList();
        // Win at once; otherwise block your row.
        foreach (var mark in new[] { "O", "X" })
            foreach (var i in free)
            {
                board[i] = mark;
                var wins = Winner(board) is not null;
                board[i] = "";
                if (wins) return i;
            }
        if (_random.NextDouble() > Skill) return free[_random.Next(free.Count)];
        var best = free[0];
        var bestScore = int.MinValue;
        foreach (var i in free)
        {
            board[i] = "O";
            var score = Minimax(board, false);
            board[i] = "";
            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }
        return best;
    }

    private static int Minimax(string[] b, bool computerTurn)
    {
        if (Winner(b) is { } row) return b[row[0]] == "O" ? 1 : -1;
        if (b.All(m => m.Length > 0)) return 0;
        var best = computerTurn ? int.MinValue : int.MaxValue;
        for (var i = 0; i < 9; i++)
        {
            if (b[i].Length > 0) continue;
            b[i] = computerTurn ? "O" : "X";
            var s = Minimax(b, !computerTurn);
            b[i] = "";
            best = computerTurn ? Math.Max(best, s) : Math.Min(best, s);
        }
        return best;
    }
}
