using System.CodeDom.Compiler;
using System.Runtime.CompilerServices;

namespace System.Text.RegularExpressions.Generated;

[GeneratedCode("System.Text.RegularExpressions.Generator", "8.0.14.16921")]
[SkipLocalsInit]
internal sealed class _003CRegexGenerator_g_003EF82295255B8CB34B513E90C02B69FEF1662077B79485DAA564721E96FA410C56C__ReFilename_0 : Regex
{
	private sealed class RunnerFactory : RegexRunnerFactory
	{
		private sealed class Runner : RegexRunner
		{
			protected override void Scan(ReadOnlySpan<char> inputSpan)
			{
				while (TryFindNextPossibleStartingPosition(inputSpan) && !TryMatchAtCurrentPosition(inputSpan) && runtextpos != inputSpan.Length)
				{
					runtextpos++;
					if (_003CRegexGenerator_g_003EF82295255B8CB34B513E90C02B69FEF1662077B79485DAA564721E96FA410C56C__Utilities.s_hasTimeout)
					{
						CheckTimeout();
					}
				}
			}

			private bool TryFindNextPossibleStartingPosition(ReadOnlySpan<char> inputSpan)
			{
				int num = runtextpos;
				if (num <= inputSpan.Length - 30)
				{
					int num2 = inputSpan.Slice(num).IndexOf("VRChat_".AsSpan());
					if (num2 >= 0)
					{
						runtextpos = num + num2;
						return true;
					}
				}
				runtextpos = inputSpan.Length;
				return false;
			}

			private bool TryMatchAtCurrentPosition(ReadOnlySpan<char> inputSpan)
			{
				int num = runtextpos;
				int start = num;
				int num2 = 0;
				int num3 = 0;
				int num4 = 0;
				ReadOnlySpan<char> span = inputSpan.Slice(num);
				if (!span.StartsWith("VRChat_".AsSpan()))
				{
					UncaptureUntil(0);
					return false;
				}
				num += 7;
				span = inputSpan.Slice(num);
				num2 = num;
				if ((uint)span.Length < 10u || !char.IsDigit(span[0]) || !char.IsDigit(span[1]) || !char.IsDigit(span[2]) || !char.IsDigit(span[3]) || span[4] != '-' || !char.IsDigit(span[5]) || !char.IsDigit(span[6]) || span[7] != '-' || !char.IsDigit(span[8]) || !char.IsDigit(span[9]))
				{
					UncaptureUntil(0);
					return false;
				}
				num += 10;
				span = inputSpan.Slice(num);
				Capture(1, num2, num);
				if (span.IsEmpty || span[0] != '_')
				{
					UncaptureUntil(0);
					return false;
				}
				num++;
				span = inputSpan.Slice(num);
				num3 = num;
				if ((uint)span.Length < 8u || !char.IsDigit(span[0]) || !char.IsDigit(span[1]) || span[2] != '-' || !char.IsDigit(span[3]) || !char.IsDigit(span[4]) || span[5] != '-' || !char.IsDigit(span[6]) || !char.IsDigit(span[7]))
				{
					UncaptureUntil(0);
					return false;
				}
				num += 8;
				span = inputSpan.Slice(num);
				Capture(2, num3, num);
				if (span.IsEmpty || span[0] != '.')
				{
					UncaptureUntil(0);
					return false;
				}
				num++;
				span = inputSpan.Slice(num);
				num4 = num;
				if ((uint)span.Length < 3u || !char.IsDigit(span[0]) || !char.IsDigit(span[1]) || !char.IsDigit(span[2]))
				{
					UncaptureUntil(0);
					return false;
				}
				num += 3;
				span = inputSpan.Slice(num);
				Capture(3, num4, num);
				runtextpos = num;
				Capture(0, start, num);
				return true;
				[MethodImpl(MethodImplOptions.AggressiveInlining)]
				void UncaptureUntil(int capturePosition)
				{
					while (Crawlpos() > capturePosition)
					{
						Uncapture();
					}
				}
			}
		}

		protected override RegexRunner CreateInstance()
		{
			return new Runner();
		}
	}

	internal static readonly _003CRegexGenerator_g_003EF82295255B8CB34B513E90C02B69FEF1662077B79485DAA564721E96FA410C56C__ReFilename_0 Instance = new _003CRegexGenerator_g_003EF82295255B8CB34B513E90C02B69FEF1662077B79485DAA564721E96FA410C56C__ReFilename_0();

	private _003CRegexGenerator_g_003EF82295255B8CB34B513E90C02B69FEF1662077B79485DAA564721E96FA410C56C__ReFilename_0()
	{
		pattern = "VRChat_(\\d{4}-\\d{2}-\\d{2})_(\\d{2}-\\d{2}-\\d{2})\\.(\\d{3})";
		roptions = RegexOptions.None;
		Regex.ValidateMatchTimeout(_003CRegexGenerator_g_003EF82295255B8CB34B513E90C02B69FEF1662077B79485DAA564721E96FA410C56C__Utilities.s_defaultTimeout);
		internalMatchTimeout = _003CRegexGenerator_g_003EF82295255B8CB34B513E90C02B69FEF1662077B79485DAA564721E96FA410C56C__Utilities.s_defaultTimeout;
		factory = new RunnerFactory();
		capsize = 4;
	}
}
