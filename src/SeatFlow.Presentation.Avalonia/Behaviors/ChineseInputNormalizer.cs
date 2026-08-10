namespace SeatFlow.Presentation.Avalonia.Behaviors;

/// <summary>
/// 全角数字/符号 → 半角转换。
/// 在 App.axaml.cs 中调用 <c>ChineseInputNormalizer.Attach(topLevel)</c> 即可全局生效。
/// </summary>
public static class ChineseInputNormalizer
{
    public static void Attach (global::Avalonia.Input.InputElement root)
    {
        root.AddHandler(global::Avalonia.Input.InputElement.TextInputEvent , OnTextInput ,
            global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private static void OnTextInput (object? sender , global::Avalonia.Input.TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text)) return;
        var normalized = Normalize(e.Text);
        if (normalized != e.Text)
            e.Text = normalized;
    }

    public static string Normalize (string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var chars = input.ToCharArray();
        bool changed = false;
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            char r = c switch
            {
                '０' => '0',
                '１' => '1',
                '２' => '2',
                '３' => '3',
                '４' => '4',
                '５' => '5',
                '６' => '6',
                '７' => '7',
                '８' => '8',
                '９' => '9',
                '，' => ',',
                '。' => '.',
                '．' => '.',
                '－' => '-',
                '；' => ';',
                '：' => ':',
                '　' => ' ',
                'Ａ' => 'A',
                'Ｂ' => 'B',
                'Ｃ' => 'C',
                'Ｄ' => 'D',
                'Ｅ' => 'E',
                'Ｆ' => 'F',
                'Ｇ' => 'G',
                'Ｈ' => 'H',
                'Ｉ' => 'I',
                'Ｊ' => 'J',
                'Ｋ' => 'K',
                'Ｌ' => 'L',
                'Ｍ' => 'M',
                'Ｎ' => 'N',
                'Ｏ' => 'O',
                'Ｐ' => 'P',
                'Ｑ' => 'Q',
                'Ｒ' => 'R',
                'Ｓ' => 'S',
                'Ｔ' => 'T',
                'Ｕ' => 'U',
                'Ｖ' => 'V',
                'Ｗ' => 'W',
                'Ｘ' => 'X',
                'Ｙ' => 'Y',
                'Ｚ' => 'Z',
                'ａ' => 'a',
                'ｂ' => 'b',
                'ｃ' => 'c',
                'ｄ' => 'd',
                'ｅ' => 'e',
                'ｆ' => 'f',
                'ｇ' => 'g',
                'ｈ' => 'h',
                'ｉ' => 'i',
                'ｊ' => 'j',
                'ｋ' => 'k',
                'ｌ' => 'l',
                'ｍ' => 'm',
                'ｎ' => 'n',
                'ｏ' => 'o',
                'ｐ' => 'p',
                'ｑ' => 'q',
                'ｒ' => 'r',
                'ｓ' => 's',
                'ｔ' => 't',
                'ｕ' => 'u',
                'ｖ' => 'v',
                'ｗ' => 'w',
                'ｘ' => 'x',
                'ｙ' => 'y',
                'ｚ' => 'z',
                _ => c
            };
            if (r != c) { chars[i] = r; changed = true; }
        }
        return changed ? new string(chars) : input;
    }
}
