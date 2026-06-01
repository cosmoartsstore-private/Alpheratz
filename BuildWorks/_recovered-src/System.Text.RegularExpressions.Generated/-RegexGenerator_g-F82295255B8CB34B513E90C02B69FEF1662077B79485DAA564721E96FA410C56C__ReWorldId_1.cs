using System.CodeDom.Compiler;
using System.Runtime.CompilerServices;

namespace System.Text.RegularExpressions.Generated;

[GeneratedCode("System.Text.RegularExpressions.Generator", "8.0.14.16921")]
[SkipLocalsInit]
internal sealed class _003CRegexGenerator_g_003EF82295255B8CB34B513E90C02B69FEF1662077B79485DAA564721E96FA410C56C__ReWorldId_1 : Regex
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
				if (num <= inputSpan.Length - 28)
				{
					int num2 = inputSpan.Slice(num).IndexOf("<vrc:WorldID>".AsSpan());
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
				ReadOnlySpan<char> span = inputSpan.Slice(num);
				if (!span.StartsWith("<vrc:WorldID>".AsSpan()))
				{
					UncaptureUntil(0);
					return false;
				}
				num += 13;
				span = inputSpan.Slice(num);
				num2 = num;
				int num3 = span.IndexOf('<');
				if (num3 < 0)
				{
					num3 = span.Length;
				}
				if (num3 == 0)
				{
					UncaptureUntil(0);
					return false;
				}
				span = span.Slice(num3);
				num += num3;
				Capture(1, num2, num);
				if (!span.StartsWith("</vrc:WorldID>".AsSpan()))
				{
					UncaptureUntil(0);
					return false;
				}
				Capture(0, start, runtextpos = num + 14);
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

	internal static readonly _003CRegexGenerator_g_003EF82295255B8CB34B513E90C02B69FEF1662077B79485DAA564721E96FA410C56C__ReWorldId_1 Instance = new _003CRegexGenerator_g_003EF82295255B8CB34B513E90C02B69FEF1662077B79485DAA564721E96FA410C56C__ReWorldId_1();

	private _003CRegexGenerator_g_003EF82295255B8CB34B513E90C02B69FEF1662077B79485DAA564721E96FA410C56C__ReWorldId_1()
	{
		pattern = "<vrc:WorldID>([^<]+)</vrc:WorldID>";
		roptions = RegexOptions.None;
		Regex.ValidateMatchTimeout(_003CRegexGenerator_g_003EF82295255B8CB34B513E90C02B69FEF1662077B79485DAA564721E96FA410C56C__Utilities.s_defaultTimeout);
		internalMatchTimeout = _003CRegexGenerator_g_003EF82295255B8CB34B513E90C02B69FEF1662077B79485DAA564721E96FA410C56C__Utilities.s_defaultTimeout;
		factory = new RunnerFactory();
		capsize = 2;
	}
}
