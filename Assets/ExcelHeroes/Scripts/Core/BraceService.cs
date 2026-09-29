using UnityEngine;
using ExcelHeroes.Data;

namespace ExcelHeroes.Core
{
    /// <summary>
    /// 괄호 수식 — ported from the web build's `openBraceFormula` / `submitBraceFormula`.
    ///
    /// The boss announces a special one swing ahead; a sum of two two-digit numbers appears; get
    /// it right inside the time limit and that special lands at 40% of its damage.
    ///
    /// It is the only thing in this game a player can do with their hands. Everything else is a
    /// number they bought earlier — levels, ★, 비품, 편성 — resolving on its own. This is four
    /// seconds where being awake is worth something, and it is deliberately arithmetic rather
    /// than a reflex test, because the disguise is a spreadsheet: the move is called 검산.
    ///
    /// The sum is two numbers from 10 to `braceMax`. Both are two digits, so the answer is always
    /// three digits or a high two — big enough that it cannot be read off, small enough to do in
    /// the head while watching a fight.
    /// </summary>
    public class BraceService
    {
        public int A { get; private set; }
        public int B { get; private set; }
        public float Left { get; private set; }
        public bool Open => Left > 0f;
        public int Answer => A + B;

        /// <summary>
        /// Four answers to pick from, shuffled, one right (the user: pick the number, not type it —
        /// a phone has no keyboard in a fight). The wrong three are the slips a head makes: a carry
        /// dropped or doubled (±10), off by one or two — so it still takes doing the sum.
        /// </summary>
        public int[] Choices { get; private set; } = new int[0];

        /// <summary>The move being announced, so the panel can name what is coming.</summary>
        public string Move { get; private set; }

        /// <summary>
        /// Poses a question. Ignored while one is already open or the charge is already held —
        /// the web's "이미 맞혔으면 또 묻지 않는다". Asking twice for the same defence would let a
        /// player bank two charges off one telegraph.
        /// </summary>
        public void Open_(string move, bool alreadyBraced)
        {
            if (Open || alreadyBraced) return;

            var b = GameData.Balance;
            var max = Mathf.Max(20, b.braceMax);
            A = Random.Range(10, max + 1);
            B = Random.Range(10, max + 1);
            Move = move;
            Left = Mathf.Max(1f, b.braceLimit);
            Choices = MakeChoices(A + B);
        }

        static readonly int[] Slips = { -10, 10, -1, 1, -2, 2, -11, 9, 11, -9 };

        static int[] MakeChoices(int answer)
        {
            var c = new System.Collections.Generic.List<int> { answer };
            var pool = new System.Collections.Generic.List<int>(Slips);
            while (c.Count < 4 && pool.Count > 0)
            {
                var i = Random.Range(0, pool.Count); var v = answer + pool[i]; pool.RemoveAt(i);
                if (v > 0 && !c.Contains(v)) c.Add(v);
            }
            for (var i = c.Count - 1; i > 0; i--) { var j = Random.Range(0, i + 1); (c[i], c[j]) = (c[j], c[i]); }
            return c.ToArray();
        }

        /// <summary>Grades a picked choice (see <see cref="Submit"/>).</summary>
        public bool Pick(int value) => Submit(value.ToString());

        /// <summary>Counts the clock down. Returns true on the frame it runs out.</summary>
        public bool Tick(float dt)
        {
            if (!Open) return false;
            Left -= dt;
            if (Left > 0f) return false;
            Left = 0f;
            return true;
        }

        public void Close() => Left = 0f;

        /// <summary>
        /// Grades an answer. Wrong or late is simply a no — the web does not punish a miss, and
        /// it should not: the move was going to land at full strength anyway, so a penalty on top
        /// would make trying worse than ignoring it.
        /// </summary>
        public bool Submit(string typed)
        {
            if (!Open) return false;
            var ok = int.TryParse(typed?.Trim(), out var v) && v == Answer;
            Close();
            return ok;
        }
    }
}
