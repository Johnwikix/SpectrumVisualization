var FoliaNativeSource = (function(exports) {

Object.defineProperty(exports, Symbol.toStringTag, { value: 'Module' });
//#region \0pretext
	function prepareWithSegments(text, font, options = {}) {
		const size = parseFloat(font.match(/([\d.]+)px/)[1]);
		const segments = Array.from(text);
		const widths = segments.map((c) => (globalThis.nativeWidths[c] ?? .6) * size + (options.letterSpacing ?? 0));
		return {
			text,
			font,
			segments,
			widths,
			breakableFitAdvances: widths.map((w) => [w])
		};
	}
	function layoutWithLines(p, width = 99999, lineHeight = 1) {
		const lines = [];
		let start = 0, advance = 0;
		const flush = (end) => {
			lines.push({
				text: p.segments.slice(start, end).join(""),
				width: advance,
				start: {
					segmentIndex: start,
					graphemeIndex: 0
				},
				end: {
					segmentIndex: end,
					graphemeIndex: 0
				}
			});
			start = end;
			advance = 0;
		};
		for (let i = 0; i < p.segments.length; i++) {
			if (p.segments[i] === "\n") {
				flush(i);
				start = i + 1;
				continue;
			}
			if (i > start && advance + p.widths[i] > width) flush(i);
			advance += p.widths[i];
		}
		if (start < p.segments.length || lines.length === 0) flush(p.segments.length);
		return {
			lines,
			lineCount: lines.length,
			height: lines.length * lineHeight
		};
	}
	const measureNaturalWidth = (p) => p.widths.reduce((a, b) => a + b, 0);

//#endregion
//#region ../../web/folia-major/src/utils/fontStacks.ts
	const MIN_FONT_WEIGHT = 100;
	const MAX_FONT_WEIGHT = 900;
	const FONT_WEIGHT_STEP = 10;
	const normalizeFontWeight = (fontWeight) => {
		if (typeof fontWeight !== "number" || !Number.isFinite(fontWeight)) return null;
		return Math.round(Math.min(900, Math.max(100, fontWeight)) / 10) * 10;
	};
	const resolveThemeFontWeight = (theme, fallback) => normalizeFontWeight(theme?.fontWeight) ?? fallback;
	const SUGAR_SERIF_FAMILY = "\"獅尾四季春加糖SC\"";
	const BUILTIN_FONT_STACKS = {
		sans: "\"Inter\", \"Noto Sans CJK SC\", \"Noto Sans JP\", \"Source Han Sans SC\", \"PingFang SC\", \"Hiragino Sans GB\", \"Microsoft YaHei\", \"Helvetica Neue\", Arial, sans-serif",
		serif: `${SUGAR_SERIF_FAMILY}, "Iowan Old Style", "Noto Serif CJK SC", "Noto Serif JP", "Source Han Serif SC", "Songti SC", "STSong", "Georgia", serif`,
		mono: "\"IBM Plex Mono\", \"Sarasa Mono SC\", \"Noto Sans Mono CJK SC\", \"Noto Sans Mono\", \"SFMono-Regular\", Consolas, monospace"
	};
	const TRANSLATION_FONT_STACKS = {
		sans: "system-ui, -apple-system, BlinkMacSystemFont, \"Segoe UI\", \"Inter\", \"Helvetica Neue\", Arial, \"Noto Sans CJK SC\", \"Source Han Sans SC\", \"PingFang SC\", \"Hiragino Sans GB\", \"Microsoft YaHei\", \"Noto Sans JP\", \"Source Han Sans JP\", \"Hiragino Sans\", \"Yu Gothic\", Meiryo, sans-serif",
		serif: `${SUGAR_SERIF_FAMILY}, "Folia Noto Serif SC", "Iowan Old Style", Georgia, "Times New Roman", "Noto Serif CJK SC", "Source Han Serif SC", "Songti SC", "STSong", "SimSun", "Noto Serif JP", "Source Han Serif JP", "Yu Mincho", "MS PMincho", serif`,
		mono: "Consolas, \"IBM Plex Mono\", \"SFMono-Regular\", Menlo, Monaco, \"Sarasa Mono SC\", \"Noto Sans Mono CJK SC\", \"SimHei\", \"DengXian\", \"Microsoft YaHei UI\", \"Microsoft YaHei\", \"Noto Sans Mono CJK JP\", \"MS Gothic\", monospace"
	};
	const CSS_GENERIC_FONT_FAMILIES = /* @__PURE__ */ new Set([
		"serif",
		"sans-serif",
		"monospace",
		"cursive",
		"fantasy",
		"system-ui",
		"ui-serif",
		"ui-sans-serif",
		"ui-monospace",
		"ui-rounded",
		"emoji",
		"math",
		"fangsong"
	]);
	const quoteFontFamily = (fontFamily) => `"${fontFamily.replace(/["\\]/g, "\\$&")}"`;
	const normalizeFontFamilyName = (fontFamily) => fontFamily.trim().replace(/^['"]|['"]$/g, "").trim();
	const formatFontFamily = (fontFamily) => {
		const normalized = normalizeFontFamilyName(fontFamily);
		if (!normalized) return null;
		return CSS_GENERIC_FONT_FAMILIES.has(normalized.toLowerCase()) ? normalized : quoteFontFamily(normalized);
	};
	const normalizeFontFamilyStack = (fontFamilies) => {
		const seen = /* @__PURE__ */ new Set();
		const stack = [];
		fontFamilies?.forEach((fontFamily) => {
			const normalized = normalizeFontFamilyName(fontFamily ?? "");
			if (!normalized) return;
			const key = normalized.toLocaleLowerCase();
			if (seen.has(key)) return;
			seen.add(key);
			stack.push(normalized);
		});
		return stack;
	};
	const buildCustomFontFamilyStack = (theme) => {
		const familiesToNormalize = [...theme.fontFamily ? [theme.fontFamily] : [], ...theme.fontFamilyStack ?? []];
		if (familiesToNormalize.length === 0) return [];
		return normalizeFontFamilyStack(familiesToNormalize).map(formatFontFamily).filter((fontFamily) => Boolean(fontFamily));
	};
	const getBuiltinThemeFontStack = (fontStyle) => {
		return BUILTIN_FONT_STACKS[fontStyle] ?? BUILTIN_FONT_STACKS.sans;
	};
	const resolveThemeFontStack = (theme) => {
		const fallbackStack = getBuiltinThemeFontStack(theme.fontStyle);
		const customFontStack = buildCustomFontFamilyStack(theme);
		if (customFontStack.length === 0) return fallbackStack;
		return `${customFontStack.join(", ")}, ${fallbackStack}`;
	};

//#endregion
//#region ../../web/folia-major/src/utils/lyrics/sentenceLayout.ts
	const CJK_REGEX$2 = /[\u4e00-\u9fa5\u3040-\u30ff\uac00-\ud7af]/;
	const hasCjkText$1 = (text) => CJK_REGEX$2.test(text);
	function findRegexMatch(regex, text, fromIndex = 0) {
		regex.lastIndex = fromIndex;
		const match = regex.exec(text);
		return match ? {
			start: match.index,
			end: match.index + match[0].length
		} : null;
	}
	function findStringMatch(str, text, fromIndex = 0) {
		const idx = text.indexOf(str, fromIndex);
		return idx === -1 ? null : {
			start: idx,
			end: idx + str.length
		};
	}
	var SentenceLayout = class SentenceLayout {
		constructor(text) {
			this.words = [];
			this.startTime = 0;
			this.endTime = 0;
			this.isSemantic = true;
			this.text = text;
		}
		static splitIntoSentences(text, targetCount, timeSeed) {
			if (targetCount <= 1 && targetCount >= 0) return [new SentenceLayout(text)];
			const maxLevel = targetCount === -1 ? 1 : targetCount === -2 ? 2 : targetCount === -3 ? 3 : targetCount === -4 ? 4 : targetCount === -5 ? 5 : 5;
			let sentences = [text];
			for (let level = 1; level <= maxLevel; level++) {
				if (sentences.length >= Math.abs(targetCount) && targetCount > 0) break;
				const newSentences = [];
				for (const sentence of sentences) newSentences.push(...SentenceLayout.splitByLevel(sentence, level));
				sentences = newSentences;
			}
			let result = sentences.map((s) => new SentenceLayout(s));
			if (targetCount > 0 && result.length < targetCount) result = SentenceLayout.secondarySplit(result, targetCount, timeSeed);
			if (result.length > 1 && result.length > targetCount && targetCount > 0) result = SentenceLayout.mergeSentences(result, targetCount);
			return result;
		}
		static splitByLevel(text, level) {
			switch (level) {
				case 1: return SentenceLayout.splitByPunctuation(text);
				case 2: return SentenceLayout.splitByBracketsQuotes(text);
				case 3: return SentenceLayout.splitByWesternWords(text);
				case 4: return SentenceLayout.splitCJKBySpace(text);
				case 5: return SentenceLayout.splitBySpecialChars(text);
				default: return [text];
			}
		}
		static splitByPunctuation(text) {
			const matches = [...text.matchAll(/[，。；！？、…·\.\,\;\!\?]+/g)];
			if (matches.length === 0) return [text];
			const result = [];
			let lastIndex = 0;
			for (const match of matches) {
				const punctStart = match.index;
				const punctEnd = punctStart + match[0].length;
				let trailingSpaces = "";
				const spaceMatch = text.slice(punctEnd).match(/^\s+/);
				if (spaceMatch) trailingSpaces = spaceMatch[0];
				const beforePunct = text.slice(lastIndex, punctStart);
				const punctWithSpaces = match[0] + trailingSpaces;
				if (beforePunct.length > 0) result.push(beforePunct + punctWithSpaces);
				else if (result.length > 0) result[result.length - 1] += punctWithSpaces;
				else result.push(punctWithSpaces);
				lastIndex = punctEnd + trailingSpaces.length;
			}
			if (lastIndex < text.length) result.push(text.slice(lastIndex));
			return result.filter((r) => r.length > 0);
		}
		static splitByWesternWords(text) {
			if (!hasCjkText$1(text)) return [text];
			const westernBlockRegex = /[a-zA-Z0-9]+(?:[a-zA-Z0-9'\-]*[a-zA-Z0-9]+)?(?:\s+[a-zA-Z0-9]+(?:[a-zA-Z0-9'\-]*[a-zA-Z0-9]+)?)+[.,;:!?。，；：！？]?\s*/g;
			SentenceLayout.charCountCache.clear();
			let hasMultipleWordBlock = false;
			let match;
			while ((match = westernBlockRegex.exec(text)) !== null) if ((match[0].match(/[a-zA-Z0-9]+/g)?.length || 0) > 1) {
				hasMultipleWordBlock = true;
				break;
			}
			if (!hasMultipleWordBlock) return [text];
			westernBlockRegex.lastIndex = 0;
			const parts = [];
			let lastIndex = 0;
			while ((match = westernBlockRegex.exec(text)) !== null) {
				const blockStart = match.index;
				const blockEnd = blockStart + match[0].length;
				const beforeBlock = text.slice(lastIndex, blockStart);
				if (SentenceLayout.shouldSkipBoundary(text, beforeBlock, blockStart)) parts.push(beforeBlock + match[0]);
				else if (blockStart > lastIndex) {
					parts.push(beforeBlock);
					parts.push(match[0]);
				} else parts.push(match[0]);
				lastIndex = blockEnd;
			}
			if (lastIndex < text.length) parts.push(text.slice(lastIndex));
			return parts.length > 1 ? parts : [text];
		}
		static {
			this.charCountCache = /* @__PURE__ */ new Map();
		}
		static getGlobalCount(text, char) {
			const key = `${text.length}:${char}`;
			if (SentenceLayout.charCountCache.has(key)) return SentenceLayout.charCountCache.get(key);
			const escaped = char.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
			const count = (text.match(new RegExp(escaped, "g")) || []).length;
			SentenceLayout.charCountCache.set(key, count);
			return count;
		}
		static shouldSkipBoundary(fullText, beforeBlock, _boundaryPos) {
			if (beforeBlock.length === 0) return false;
			const sepChar = beforeBlock[beforeBlock.length - 1];
			if (sepChar === "-") return true;
			if (sepChar === ":" || sepChar === "：" || sepChar === "/" || sepChar === "／" || sepChar === "|" || sepChar === "｜") return SentenceLayout.getGlobalCount(fullText, sepChar) >= 2;
			return false;
		}
		static splitWesternPhrase(text) {
			if (!hasCjkText$1(text) && text.length > 0) {
				const parts = text.split(/(?<=\w[.,;:!?。，；：！？])(?=\s+\w)/);
				if (parts.length > 1) return parts.filter((p) => p.length > 0);
			}
			return [text];
		}
		static splitByBracketsQuotes(text) {
			const result = SentenceLayout.extractOutermostPairs(text, [
				{
					open: /「/,
					close: /」/
				},
				{
					open: /『/,
					close: /』/
				},
				{
					open: /《/,
					close: /》/
				},
				{
					open: /【/,
					close: /】/
				},
				{
					open: /｛/,
					close: /｝/
				},
				{
					open: /［/,
					close: /］/
				},
				{
					open: /\[/,
					close: /\]/
				},
				{
					open: /（/,
					close: /）/
				},
				{
					open: /\(/,
					close: /\)/
				},
				{
					openStr: "\"",
					closeStr: "\""
				},
				{
					openStr: "'",
					closeStr: "'"
				}
			]);
			return result.length > 1 ? result : [text];
		}
		static extractOutermostPairs(text, defs) {
			const best = SentenceLayout.findOutermostPair(text, defs);
			if (!best) return [text];
			const before = text.slice(0, best.openStart);
			const pairedContent = text.slice(best.openStart, best.closeEnd);
			const remainder = text.slice(best.closeEnd);
			const result = [];
			if (before.length > 0) result.push(...SentenceLayout.extractOutermostPairs(before, defs));
			result.push(pairedContent);
			if (remainder.length > 0) result.push(...SentenceLayout.extractOutermostPairs(remainder, defs));
			return result.filter((p) => p.length > 0);
		}
		static findOutermostPair(text, defs) {
			let best = null;
			for (const def of defs) {
				const openMatch = def.openStr !== void 0 ? findStringMatch(def.openStr, text) : findRegexMatch(def.open, text);
				if (!openMatch) continue;
				const searchFrom = openMatch.end;
				const closeMatch = def.closeStr !== void 0 ? findStringMatch(def.closeStr, text, searchFrom) : findRegexMatch(def.close, text, searchFrom);
				if (!closeMatch) continue;
				const candidate = {
					openStart: openMatch.start,
					openEnd: openMatch.end,
					closeStart: closeMatch.start,
					closeEnd: closeMatch.end
				};
				if (!best || candidate.openStart < best.openStart) best = candidate;
				else if (candidate.openStart === best.openStart && candidate.closeEnd > best.closeEnd) best = candidate;
			}
			return best;
		}
		static splitCJKBySpace(text) {
			if (!hasCjkText$1(text)) return [text];
			const segments = [];
			let currentSegment = "";
			let inCjkBlock = false;
			for (let i = 0; i < text.length; i++) {
				const char = text[i];
				const isCJK = CJK_REGEX$2.test(char);
				const isWestern = /[a-zA-Z0-9]/.test(char);
				const isSpace = /\s/.test(char);
				if (isCJK) {
					currentSegment += char;
					inCjkBlock = true;
				} else if (isWestern) currentSegment += char;
				else if (isSpace) {
					if (char === "　" && currentSegment.length > 0) {
						currentSegment += char;
						segments.push(currentSegment);
						currentSegment = "";
						inCjkBlock = false;
					} else if (inCjkBlock && currentSegment.length > 0) {
						const lastChar = currentSegment[currentSegment.length - 1];
						if (CJK_REGEX$2.test(lastChar)) {
							currentSegment += char;
							segments.push(currentSegment);
							currentSegment = "";
							inCjkBlock = false;
						} else currentSegment += char;
					} else if (currentSegment.length === 0 && segments.length > 0) segments[segments.length - 1] += char;
					else currentSegment += char;
				} else if (currentSegment.length === 0 && segments.length > 0) segments[segments.length - 1] += char;
				else currentSegment += char;
			}
			if (currentSegment) {
				if (segments.length > 0 && /^\s+$/.test(currentSegment)) segments[segments.length - 1] += currentSegment;
				else segments.push(currentSegment);
			}
			return segments.length > 1 ? segments : [text];
		}
		static splitBySpecialChars(text) {
			const parts = text.split(/[：:\/／\\|｜~～]+/);
			if (parts.filter((part) => part.length > 0).length <= 1) return [text];
			const result = [];
			let lastIndex = 0;
			for (const part of parts) {
				const index = text.indexOf(part, lastIndex);
				if (index > lastIndex) {
					const specialChar = text.slice(lastIndex, index);
					if (result.length > 0) result[result.length - 1] += specialChar;
					else result.push(specialChar);
				}
				if (part) {
					result.push(part);
					lastIndex = index + part.length;
				} else if (index !== -1) lastIndex = index;
			}
			if (lastIndex < text.length) {
				const tail = text.slice(lastIndex);
				if (tail && result.length > 0) result[result.length - 1] += tail;
				else if (tail) result.push(tail);
			}
			return result.filter((r) => r.length > 0);
		}
		static secondarySplit(sentences, targetCount, timeSeed) {
			const Segmenter = Intl?.Segmenter;
			const segmenter = Segmenter ? new Segmenter(void 0, { granularity: "word" }) : null;
			while (sentences.length < targetCount) {
				const candidates = sentences.filter((s) => s.text.length > 2);
				if (candidates.length === 0) break;
				if (segmenter) {
					const semanticCandidates = [];
					for (const s of candidates) try {
						const segments = Array.from(segmenter.segment(s.text));
						const wordPositions = [];
						let offset = 0;
						for (const seg of segments) {
							if (seg.isWordLike) wordPositions.push({
								start: offset,
								end: offset + seg.segment.length
							});
							offset += seg.segment.length;
						}
						if (wordPositions.length >= 2) {
							const midChar = s.text.length / 2;
							let bestGapIdx = 1;
							let bestDist = Infinity;
							for (let g = 1; g < wordPositions.length; g++) {
								const dist = Math.abs(wordPositions[g].start - midChar);
								if (dist < bestDist) {
									bestDist = dist;
									bestGapIdx = g;
								}
							}
							const splitPos = wordPositions[bestGapIdx].start;
							const balanceScore = 1 - Math.abs(splitPos - midChar) / midChar;
							semanticCandidates.push({
								sentence: s,
								index: sentences.indexOf(s),
								splitPos,
								score: balanceScore * 10 + wordPositions.length
							});
						}
					} catch {}
					if (semanticCandidates.length > 0) {
						semanticCandidates.sort((a, b) => b.score - a.score);
						const best = semanticCandidates[0];
						const firstHalf = best.sentence.text.slice(0, best.splitPos);
						const secondHalf = best.sentence.text.slice(best.splitPos);
						sentences.splice(best.index, 1, new SentenceLayout(firstHalf), new SentenceLayout(secondHalf));
						continue;
					}
				}
				const pseudoRandom = (seed) => {
					const x = Math.sin(seed) * 1e4;
					return x - Math.floor(x);
				};
				const seed = sentences.reduce((acc, s) => acc + s.text.split("").reduce((sum, c) => sum + c.charCodeAt(0), 0), 0) + sentences.length + (timeSeed ?? 0);
				const selectedCandidate = candidates[Math.floor(pseudoRandom(seed) * candidates.length)];
				const candidateIndex = sentences.indexOf(selectedCandidate);
				const midPoint = Math.floor(selectedCandidate.text.length / 2);
				const firstHalf = selectedCandidate.text.slice(0, midPoint);
				const secondHalf = selectedCandidate.text.slice(midPoint);
				sentences.splice(candidateIndex, 1, new SentenceLayout(firstHalf), new SentenceLayout(secondHalf));
			}
			return sentences;
		}
		static mergeSentences(sentences, targetCount) {
			while (sentences.length > targetCount) {
				let bestMergeIndex = 0;
				let shortestCombinedLength = Infinity;
				for (let i = 0; i < sentences.length - 1; i++) {
					const combinedLength = sentences[i].text.length + sentences[i + 1].text.length;
					if (combinedLength < shortestCombinedLength) {
						shortestCombinedLength = combinedLength;
						bestMergeIndex = i;
					}
				}
				const merged = new SentenceLayout(sentences[bestMergeIndex].text + sentences[bestMergeIndex + 1].text);
				sentences.splice(bestMergeIndex, 2, merged);
			}
			return sentences;
		}
	};

//#endregion
//#region .tools/folia-native-tilt.ts
	const LOG_OFFSET = 4;
	const LINE_THRESHOLDS = [
		.45,
		1.05,
		1.7
	];
	const determineLineCount = (charCount, seed, splitProbability) => {
		const score = Math.log(charCount + LOG_OFFSET) / Math.log(24) * (seededRandom(seed, 1) * .6 + .7) * splitProbability;
		if (score < LINE_THRESHOLDS[0]) return 1;
		if (score < LINE_THRESHOLDS[1]) return 2;
		if (score < LINE_THRESHOLDS[2]) return 3;
		return 4;
	};
	const REM_PX = 16;
	const RESPLIT_THRESHOLD = 1.6;
	const SCALE_FLOOR_NORMAL = .55;
	const SCALE_FLOOR_TILT = .5;
	const getAvailableWidth = () => {
		if (typeof window === "undefined") return 1200;
		return Math.max(320, window.innerWidth) * .85;
	};
	const measureAtSize = (text, pxSize, fontSpec) => {
		const prepared = prepareWithSegments(text, fontSpec);
		return layoutWithLines(prepared, 99999, pxSize * 1.4).lines[0]?.width ?? text.length * pxSize * .6;
	};
	const seededRandom = (seed, offset) => {
		const x = Math.sin(seed * 1e3 + offset) * 1e4;
		return x - Math.floor(x);
	};
	const buildTiltLayout = (fullText, lineSeed, tuning, theme, fontScale) => {
		const fontStack = resolveThemeFontStack(theme);
		const viewportWidth = typeof window !== "undefined" ? window.innerWidth : 1200;
		const measureMaxPx = Math.max(viewportWidth * .06875, 5.625 * REM_PX * fontScale);
		const normalBasePx = measureMaxPx;
		const tiltBasePx = measureMaxPx;
		const normalFontSpec = `${resolveThemeFontWeight(theme, 400)} ${normalBasePx}px ${fontStack}`;
		const tiltFontSpec = `${resolveThemeFontWeight(theme, 300)} ${tiltBasePx}px ${fontStack} italic`;
		const availableWidth = getAvailableWidth();
		const charCount = fullText.trim().length;
		const isEllipsisOnly = /^[\s.…·。]+$/.test(fullText.trim());
		let mergedSegments;
		if (isEllipsisOnly) mergedSegments = [fullText];
		else {
			const numLines = determineLineCount(charCount, lineSeed, tuning.splitProbability);
			mergedSegments = SentenceLayout.splitIntoSentences(fullText, numLines, lineSeed).map((u) => u.text);
		}
		const candidates = [];
		mergedSegments.forEach((_, i) => {
			if (seededRandom(lineSeed, 100 + i) < tuning.tiltStyleProbability) candidates.push(i);
		});
		let finalTiltIndex = -1;
		if (candidates.length > 0) finalTiltIndex = candidates[Math.floor(seededRandom(lineSeed, 200) * candidates.length)];
		let offsetAccum = 0;
		const segments = mergedSegments.map((text, i) => {
			const trimmed = text.trimStart().trimEnd();
			const leadingSpaces = text.length - text.trimStart().length;
			const seg = {
				text: trimmed,
				isTilt: i === finalTiltIndex,
				isShortLastLine: false,
				charOffset: offsetAccum + leadingSpaces
			};
			offsetAccum += text.length;
			return seg;
		});
		let scaleMultiplier = 1;
		const tiltWithWidth = segments.filter((s) => s.isTilt).map((s) => ({
			text: s.text,
			width: measureAtSize(s.text, tiltBasePx, tiltFontSpec)
		}));
		const normalWithWidth = segments.filter((s) => !s.isTilt).map((s) => ({
			text: s.text,
			width: measureAtSize(s.text, normalBasePx, normalFontSpec)
		}));
		const widestTiltEntry = tiltWithWidth.sort((a, b) => b.width - a.width)[0];
		const widestNormalEntry = normalWithWidth.sort((a, b) => b.width - a.width)[0];
		const tiltWidth = widestTiltEntry?.width ?? 0;
		const normalWidth = widestNormalEntry?.width ?? 0;
		const tiltOverflow = tiltWidth > 0 ? tiltWidth / availableWidth : 0;
		const normalOverflow = normalWidth > 0 ? normalWidth / availableWidth : 0;
		const maxOverflow = Math.max(tiltOverflow, normalOverflow);
		const markShortLastLine = (segs) => {
			if (segs.length < 2) return segs;
			const last = segs[segs.length - 1];
			if (last.isTilt || last.text.trim().length > 2) return segs;
			const prev = segs[segs.length - 2];
			if (last.text.trim().length * 2 <= prev.text.length) segs[segs.length - 1] = {
				...last,
				isShortLastLine: true
			};
			return segs;
		};
		if (maxOverflow > 1) {
			if (maxOverflow >= RESPLIT_THRESHOLD) {
				const targetWidth = availableWidth * 1.4500000000000002;
				const totalEstWidth = measureAtSize(fullText, normalBasePx, normalFontSpec);
				const extraSplitsNeeded = Math.min(4, Math.max(segments.length + 1, Math.ceil(totalEstWidth / targetWidth)));
				if (extraSplitsNeeded > segments.length) {
					const reSplitUnits = SentenceLayout.splitIntoSentences(fullText, extraSplitsNeeded, lineSeed);
					let reSplitOffset = 0;
					const newSegments = reSplitUnits.map((u) => {
						const trimmed = u.text.trimStart().trimEnd();
						const leadingSpaces = u.text.length - u.text.trimStart().length;
						const seg = {
							text: trimmed,
							isTilt: false,
							isShortLastLine: false,
							charOffset: reSplitOffset + leadingSpaces
						};
						reSplitOffset += u.text.length;
						return seg;
					});
					if (newSegments.length > 0) {
						if (seededRandom(lineSeed, 300) < tuning.tiltStyleProbability) {
							const picked = Math.floor(seededRandom(lineSeed, 301) * newSegments.length);
							newSegments[picked].isTilt = true;
						}
					}
					const postWidestTilt = newSegments.filter((s) => s.isTilt)[0];
					const postWidestNormal = newSegments.filter((s) => !s.isTilt).map((s) => ({
						text: s.text,
						width: measureAtSize(s.text, normalBasePx, normalFontSpec)
					})).sort((a, b) => b.width - a.width)[0];
					let postScale = 1;
					if (postWidestTilt) {
						const w = measureAtSize(postWidestTilt.text, tiltBasePx, tiltFontSpec);
						if (w > availableWidth) postScale = Math.max(SCALE_FLOOR_TILT, availableWidth / w);
					} else if (postWidestNormal) {
						const w = measureAtSize(postWidestNormal.text, normalBasePx, normalFontSpec);
						if (w > availableWidth) postScale = Math.max(SCALE_FLOOR_NORMAL, availableWidth / w);
					}
					return {
						segments: markShortLastLine(newSegments),
						scaleMultiplier: postScale
					};
				}
			}
			if (tiltOverflow >= normalOverflow && widestTiltEntry) scaleMultiplier = Math.max(SCALE_FLOOR_TILT, availableWidth / tiltWidth);
			else if (widestNormalEntry) scaleMultiplier = Math.max(SCALE_FLOOR_NORMAL, availableWidth / normalWidth);
		}
		return {
			segments: markShortLastLine(segments),
			scaleMultiplier
		};
	};

//#endregion
//#region ../../web/folia-major/src/utils/colorPalette.ts
	const QUANTIZATION_SHIFT = 4;
	const MIN_ALPHA = 128;
	const getChannelRange = (colors, channel) => {
		let min = 255;
		let max = 0;
		for (const color of colors) {
			min = Math.min(min, color[channel]);
			max = Math.max(max, color[channel]);
		}
		return max - min;
	};
	const splitBucket = (bucket) => {
		if (bucket.colors.length < 2) return null;
		const ranges = {
			r: getChannelRange(bucket.colors, "r"),
			g: getChannelRange(bucket.colors, "g"),
			b: getChannelRange(bucket.colors, "b")
		};
		const channel = Object.keys(ranges).reduce((widest, candidate) => ranges[candidate] > ranges[widest] ? candidate : widest, "r");
		const sorted = [...bucket.colors].sort((a, b) => a[channel] - b[channel]);
		const midpoint = bucket.weight / 2;
		let accumulatedWeight = 0;
		let splitIndex = 1;
		for (let index = 0; index < sorted.length - 1; index += 1) {
			accumulatedWeight += sorted[index].weight;
			splitIndex = index + 1;
			if (accumulatedWeight >= midpoint) break;
		}
		const left = sorted.slice(0, splitIndex);
		const right = sorted.slice(splitIndex);
		return [{
			colors: left,
			weight: left.reduce((sum, color) => sum + color.weight, 0)
		}, {
			colors: right,
			weight: right.reduce((sum, color) => sum + color.weight, 0)
		}];
	};
	const averageBucket = (bucket) => {
		const totals = bucket.colors.reduce((result, color) => ({
			r: result.r + color.r * color.weight,
			g: result.g + color.g * color.weight,
			b: result.b + color.b * color.weight
		}), {
			r: 0,
			g: 0,
			b: 0
		});
		return {
			r: Math.round(totals.r / bucket.weight),
			g: Math.round(totals.g / bucket.weight),
			b: Math.round(totals.b / bucket.weight),
			weight: bucket.weight
		};
	};
	const toHex = ({ r, g, b }) => `#${r.toString(16).padStart(2, "0")}${g.toString(16).padStart(2, "0")}${b.toString(16).padStart(2, "0")}`;
	const extractRepresentativeColorsFromPixels = (pixels, count = 5) => {
		if (count <= 0) return [];
		const histogram = /* @__PURE__ */ new Map();
		for (let index = 0; index < pixels.length; index += 4) {
			if (pixels[index + 3] < MIN_ALPHA) continue;
			const r = pixels[index];
			const g = pixels[index + 1];
			const b = pixels[index + 2];
			const key = r >> QUANTIZATION_SHIFT << 8 | g >> QUANTIZATION_SHIFT << 4 | b >> QUANTIZATION_SHIFT;
			const existing = histogram.get(key);
			if (existing) {
				existing.r += r;
				existing.g += g;
				existing.b += b;
				existing.weight += 1;
			} else histogram.set(key, {
				r,
				g,
				b,
				weight: 1
			});
		}
		const colors = [...histogram.values()].map((color) => ({
			r: Math.round(color.r / color.weight),
			g: Math.round(color.g / color.weight),
			b: Math.round(color.b / color.weight),
			weight: color.weight
		}));
		if (colors.length === 0) return [];
		const buckets = [{
			colors,
			weight: colors.reduce((sum, color) => sum + color.weight, 0)
		}];
		while (buckets.length < Math.min(count, colors.length)) {
			let bucketIndex = -1;
			let bestScore = -1;
			for (let index = 0; index < buckets.length; index += 1) {
				const bucket = buckets[index];
				if (bucket.colors.length < 2) continue;
				const score = Math.max(getChannelRange(bucket.colors, "r"), getChannelRange(bucket.colors, "g"), getChannelRange(bucket.colors, "b")) * Math.sqrt(bucket.weight);
				if (score > bestScore) {
					bestScore = score;
					bucketIndex = index;
				}
			}
			if (bucketIndex < 0) break;
			const split = splitBucket(buckets[bucketIndex]);
			if (!split) break;
			buckets.splice(bucketIndex, 1, ...split);
		}
		return buckets.map(averageBucket).sort((a, b) => b.weight - a.weight).map(toHex);
	};

//#endregion
//#region ../../web/folia-major/src/utils/lyrics/renderHints.ts
	const MICRO_LINE_DURATION_THRESHOLD = .1;
	const SHORT_LINE_DURATION_THRESHOLD = .18;
	const MICRO_LINE_RENDER_FLOOR = .067;
	const getLastWordEndTime = (line) => {
		return (line.words?.[line.words.length - 1])?.endTime ?? line.endTime;
	};
	const getLineTransitionTiming = (rawDuration, lineTransitionMode, wordRevealMode) => {
		if (lineTransitionMode === "none") return {
			enterDuration: 0,
			exitDuration: 0,
			linePassHold: 0
		};
		if (lineTransitionMode === "fast") return {
			enterDuration: clamp$11(rawDuration * .45, .045, .06),
			exitDuration: clamp$11(rawDuration * .22, .03, .04),
			linePassHold: wordRevealMode === "instant" ? 0 : .03
		};
		return {
			enterDuration: Math.min(.42, Math.max(.22, Math.max(rawDuration, .12) * .34)),
			exitDuration: Math.min(.32, Math.max(.18, Math.max(rawDuration, .12) * .18)),
			linePassHold: wordRevealMode === "instant" ? 0 : .06
		};
	};
	const getTimingClass = (rawDuration) => {
		if (rawDuration < .1) return "micro";
		if (rawDuration < .18) return "short";
		return "normal";
	};
	const getLineTransitionMode = (timingClass) => {
		if (timingClass === "micro") return "none";
		if (timingClass === "short") return "fast";
		return "normal";
	};
	const getWordRevealMode = (timingClass) => {
		if (timingClass === "micro") return "instant";
		if (timingClass === "short") return "fast";
		return "normal";
	};
	const clamp$11 = (value, min, max) => Math.min(Math.max(value, min), max);
	const buildLineRenderEndTime = (line, rawDuration, lineTransitionMode, wordRevealMode) => {
		if (lineTransitionMode === "none") return Math.max(line.endTime, line.startTime + MICRO_LINE_RENDER_FLOOR);
		const transitionTiming = getLineTransitionTiming(rawDuration, lineTransitionMode, wordRevealMode);
		const linePassStart = Math.max(getLastWordEndTime(line), line.startTime) + transitionTiming.linePassHold;
		const exitStart = lineTransitionMode === "fast" ? Math.max(line.startTime + transitionTiming.enterDuration + .01, linePassStart, line.endTime - transitionTiming.exitDuration) : Math.max(linePassStart, line.endTime - transitionTiming.exitDuration);
		return Math.max(line.endTime, exitStart + transitionTiming.exitDuration);
	};
	function buildLineRenderHints(lineOrStart, endTime) {
		const line = typeof lineOrStart === "number" ? {
			startTime: lineOrStart,
			endTime: endTime ?? lineOrStart
		} : lineOrStart;
		const rawDuration = Math.max(line.endTime - line.startTime, 0);
		const timingClass = getTimingClass(rawDuration);
		const lineTransitionMode = getLineTransitionMode(timingClass);
		const wordRevealMode = getWordRevealMode(timingClass);
		return {
			rawDuration,
			timingClass,
			renderEndTime: buildLineRenderEndTime(line, rawDuration, lineTransitionMode, wordRevealMode),
			lineTransitionMode,
			wordRevealMode
		};
	}
	const getLineRenderHints = (line) => {
		if (!line) return null;
		return line.renderHints ?? buildLineRenderHints(line);
	};
	const getLineRenderEndTime = (line) => {
		if (!line) return Number.NEGATIVE_INFINITY;
		return getLineRenderHints(line)?.renderEndTime ?? line.endTime;
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/textMeasureCache.ts
/**
	* Stores `value` under `key` in an insertion-ordered Map, first evicting the oldest entry when a new
	* key would push the cache past `limit`. Returns `value` so callers can `return rememberBounded(...)`.
	*/
	const rememberBounded = (cache, key, value, limit) => {
		if (!cache.has(key) && cache.size >= limit) {
			const oldest = cache.keys().next();
			if (!oldest.done) cache.delete(oldest.value);
		}
		cache.set(key, value);
		return value;
	};
	/**
	* Cumulative grapheme offsets: `offsets[i]` is the advance of the first `i` graphemes, with
	* `offsets[0] = 0`. Each prefix is measured as a whole string (not as a sum of single glyphs) so
	* kerning and shaping across glyph boundaries stay exact; `measurePrefix` also receives the count and
	* the previous offset for modes that add tracking or keep the offsets monotonic.
	*/
	const measurePrefixOffsets = (graphemes, measurePrefix) => {
		const offsets = new Array(graphemes.length + 1).fill(0);
		for (let count = 1; count <= graphemes.length; count += 1) offsets[count] = measurePrefix(graphemes.slice(0, count).join(""), count, offsets[count - 1]);
		return offsets;
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/colorMix.ts
	const clamp$10 = (value, min, max) => Math.max(min, Math.min(max, value));
	const mix$2 = (from, to, amount) => from + (to - from) * amount;
	const FALLBACK_RGB = {
		r: 255,
		g: 255,
		b: 255
	};
	const isFiniteChannel = (value) => Number.isFinite(value);
	const formatRgba = (channels, alpha) => `rgba(${Math.round(clamp$10(channels.r, 0, 255))}, ${Math.round(clamp$10(channels.g, 0, 255))}, ${Math.round(clamp$10(channels.b, 0, 255))}, ${alpha})`;
	const readColor = (normalizedColor) => {
		if (!normalizedColor) return "fallback";
		if (normalizedColor.startsWith("#")) {
			const hex = normalizedColor.slice(1);
			const parse = (value) => Number.parseInt(value, 16);
			if (/^[0-9a-fA-F]{3}$/.test(hex)) return {
				r: parse(hex[0] + hex[0]),
				g: parse(hex[1] + hex[1]),
				b: parse(hex[2] + hex[2])
			};
			if (/^[0-9a-fA-F]{6}$/.test(hex)) return {
				r: parse(hex.slice(0, 2)),
				g: parse(hex.slice(2, 4)),
				b: parse(hex.slice(4, 6))
			};
			return "fallback";
		}
		const rgbMatch = normalizedColor.match(/^rgba?\(([^)]+)\)$/);
		if (rgbMatch) {
			const [r, g, b] = rgbMatch[1].split(",").slice(0, 3).map((part) => Number.parseFloat(part.trim()));
			if ([
				r,
				g,
				b
			].every(isFiniteChannel)) return {
				r,
				g,
				b
			};
			return "fallback";
		}
		return "passthrough";
	};
	const parsedColors = /* @__PURE__ */ new Map();
	const PARSED_COLOR_LIMIT = 512;
	const parseColor = (color) => {
		const normalized = typeof color === "string" ? color.trim() : "";
		let parsed = parsedColors.get(normalized);
		if (parsed === void 0) parsed = rememberBounded(parsedColors, normalized, readColor(normalized), PARSED_COLOR_LIMIT);
		return {
			normalized,
			parsed
		};
	};
	const colorWithAlpha = (color, alpha) => {
		const normalizedAlpha = clamp$10(alpha, 0, 1);
		const { normalized, parsed } = parseColor(color);
		if (parsed === "passthrough") return normalized;
		return formatRgba(parsed === "fallback" ? FALLBACK_RGB : parsed, normalizedAlpha);
	};
	/** The colour's channels, or null when it is not a hex or rgb() colour. Returns a fresh object. */
	const parseColorChannels = (color) => {
		const { parsed } = parseColor(color);
		return typeof parsed === "string" ? null : { ...parsed };
	};
	const mixColors = (from, to, amount, alpha = 1) => {
		const normalizedAmount = clamp$10(amount, 0, 1);
		const fromParsed = parseColor(from).parsed;
		const toParsed = parseColor(to).parsed;
		if (typeof fromParsed === "string" || typeof toParsed === "string") return colorWithAlpha(normalizedAmount >= .5 ? to : from, alpha);
		return `rgba(${Math.round(mix$2(fromParsed.r, toParsed.r, normalizedAmount))}, ${Math.round(mix$2(fromParsed.g, toParsed.g, normalizedAmount))}, ${Math.round(mix$2(fromParsed.b, toParsed.b, normalizedAmount))}, ${clamp$10(alpha, 0, 1)})`;
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/cadenza/cadenzaEnvelopes.ts
	const graphemeSegmenter$2 = typeof Intl !== "undefined" ? new Intl.Segmenter(void 0, { granularity: "grapheme" }) : null;
	const splitGraphemes$2 = (text) => {
		if (!text) return [];
		if (graphemeSegmenter$2) return Array.from(graphemeSegmenter$2.segment(text), ({ segment }) => segment);
		return Array.from(text);
	};
	const clamp$9 = (value, min, max) => Math.max(min, Math.min(max, value));
	const isCJK$1 = (text) => /[\u4e00-\u9fa5\u3040-\u30ff\uac00-\ud7af]/.test(text);

//#endregion
//#region \0graphemes
	const buildWordGraphemeTimings = (word) => Array.from(word.text).map((char, i, chars) => ({
		char,
		startTime: word.startTime + (word.endTime - word.startTime) * i / chars.length,
		endTime: word.startTime + (word.endTime - word.startTime) * (i + 1) / chars.length
	}));
	const buildLineGraphemeTimeline = (line) => (line._segments ?? []).flatMap((s) => s.graphemes);
	const splitLyricGraphemes = (text) => {
		const line = globalThis.nativeLines?.find((l) => l.fullText === text);
		return line ? line._segments.flatMap((s) => s.graphemes.map((g) => g.char)) : Array.from(text);
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/wordColoring.ts
	const CJK_REGEX$1 = /[\u4e00-\u9fa5\u3040-\u30ff\uac00-\ud7af]/;
	const isCJK = (text) => CJK_REGEX$1.test(text);
	const normalizeWordColorToken = (text) => text.toLowerCase().replace(/[^\w]/g, "");
	const resolveWordColorEntryText = (entry) => {
		if (!entry || typeof entry !== "object" || !("word" in entry)) return "";
		const word = entry.word;
		return typeof word === "string" ? word.trim() : "";
	};
	const resolveWordColorEntryColor = (entry) => {
		if (!entry || typeof entry !== "object" || !("color" in entry)) return "";
		const color = entry.color;
		return typeof color === "string" ? color : "";
	};
	const rangesOverlap = (a, b) => a.startOffset < b.endOffset && b.startOffset < a.endOffset;
	const selectNonOverlappingRanges = (ranges) => {
		const selected = [];
		[...ranges].sort((a, b) => b.priority - a.priority || a.startOffset - b.startOffset || a.endOffset - b.endOffset).forEach((range) => {
			if (!selected.some((current) => rangesOverlap(current, range))) selected.push(range);
		});
		return selected.sort((a, b) => a.startOffset - b.startOffset || a.endOffset - b.endOffset);
	};
	/** Builds phrase color ranges once per line so token renderers can resolve colors with a single ordered pass. */
	const buildWordColorRangesFromMatchers = (lineText, matchers) => {
		if (!lineText || matchers.length === 0) return [];
		const ranges = [];
		const englishColorByWord = /* @__PURE__ */ new Map();
		matchers.forEach((matcher) => {
			matcher.cjkPhrases.forEach((target) => {
				let cursor = 0;
				while (cursor < lineText.length) {
					const startOffset = lineText.indexOf(target, cursor);
					if (startOffset < 0) break;
					ranges.push({
						startOffset,
						endOffset: startOffset + target.length,
						color: matcher.color,
						priority: matcher.priority
					});
					cursor = startOffset + Math.max(target.length, 1);
				}
			});
			matcher.englishWords.forEach((word) => {
				const current = englishColorByWord.get(word);
				if (!current || matcher.priority > current.priority) englishColorByWord.set(word, {
					color: matcher.color,
					priority: matcher.priority
				});
			});
		});
		if (englishColorByWord.size > 0) {
			const wordRegex = /\w+/g;
			let match;
			while ((match = wordRegex.exec(lineText)) !== null) {
				const wordColor = englishColorByWord.get(normalizeWordColorToken(match[0]));
				if (wordColor) ranges.push({
					startOffset: match.index,
					endOffset: match.index + match[0].length,
					color: wordColor.color,
					priority: wordColor.priority
				});
			}
		}
		return selectNonOverlappingRanges(ranges);
	};
	const resolveWordColor = (wordText, wordColors, fallbackColor, { keywordColoringEnabled = true, cjkMatchMode = "target-contains-token" } = {}) => {
		if (!keywordColoringEnabled || !wordColors || wordColors.length === 0) return fallbackColor;
		const cleanCurrent = wordText.trim();
		if (!cleanCurrent) return fallbackColor;
		const matched = wordColors.find((entry) => {
			const target = resolveWordColorEntryText(entry);
			if (!target) return false;
			if (isCJK(cleanCurrent)) {
				if (cjkMatchMode === "exact") return target === cleanCurrent;
				if (cjkMatchMode === "bidirectional-contains") return target.includes(cleanCurrent) || cleanCurrent.includes(target);
				return target.includes(cleanCurrent);
			}
			const targetWords = target.split(/\s+/).map(normalizeWordColorToken).filter(Boolean);
			const normalizedCurrent = normalizeWordColorToken(cleanCurrent);
			return Boolean(normalizedCurrent) && targetWords.includes(normalizedCurrent);
		});
		return resolveWordColorEntryColor(matched) || fallbackColor;
	};
	const resolveTokenColorMap = (tokens, ranges) => {
		const colors = /* @__PURE__ */ new Map();
		let rangeIndex = 0;
		tokens.forEach((token) => {
			if (!token.timed) return;
			while (rangeIndex < ranges.length && ranges[rangeIndex].endOffset <= token.startOffset) rangeIndex += 1;
			const range = ranges[rangeIndex];
			if (range && rangesOverlap(range, token)) colors.set(token.key, range.color);
		});
		return colors;
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/cadenza/cadenzaFragments.ts
	const getActiveColor = (wordText, theme) => {
		return resolveWordColor(wordText, theme.wordColors, theme.accentColor);
	};
	const buildSegmentMetas$1 = (prepared) => {
		const segmentMetas = [];
		const graphemes = [];
		let graphemeCursor = 0;
		for (const segment of prepared.segments) {
			const segmentGraphemes = splitGraphemes$2(segment);
			segmentMetas.push({
				graphemeStart: graphemeCursor,
				graphemeEnd: graphemeCursor + segmentGraphemes.length,
				graphemeCount: segmentGraphemes.length
			});
			graphemes.push(...segmentGraphemes);
			graphemeCursor += segmentGraphemes.length;
		}
		return {
			segmentMetas,
			graphemes
		};
	};
	const findWordRanges = (line, graphemes, theme) => {
		const ranges = [];
		let cursor = 0;
		for (let wordIndex = 0; wordIndex < line.words.length; wordIndex++) {
			const word = line.words[wordIndex];
			const target = splitGraphemes$2(word.text);
			let start = -1;
			for (let i = cursor; i <= graphemes.length - target.length; i++) {
				let isMatch = true;
				for (let j = 0; j < target.length; j++) if (graphemes[i + j] !== target[j]) {
					isMatch = false;
					break;
				}
				if (isMatch) {
					start = i;
					break;
				}
			}
			if (start === -1) start = clamp$9(cursor, 0, graphemes.length);
			const end = clamp$9(start + target.length, start, graphemes.length);
			ranges.push({
				wordIndex,
				word,
				start,
				end,
				color: getActiveColor(word.text, theme),
				graphemeTimings: buildWordGraphemeTimings(word)
			});
			cursor = end;
		}
		return ranges;
	};
	const cursorToGlobalOffset$1 = (cursor, segmentMetas) => {
		if (segmentMetas.length === 0) return 0;
		const segment = segmentMetas[cursor.segmentIndex];
		if (!segment) return segmentMetas[segmentMetas.length - 1].graphemeEnd;
		return clamp$9(segment.graphemeStart + cursor.graphemeIndex, segment.graphemeStart, segment.graphemeEnd);
	};
	const getPartialSegmentWidth$1 = (prepared, segmentIndex, segmentMeta, startOffset, endOffset) => {
		const localStart = clamp$9(startOffset - segmentMeta.graphemeStart, 0, segmentMeta.graphemeCount);
		const localEnd = clamp$9(endOffset - segmentMeta.graphemeStart, 0, segmentMeta.graphemeCount);
		if (localEnd <= localStart) return 0;
		if (localStart === 0 && localEnd === segmentMeta.graphemeCount) return prepared.widths[segmentIndex] ?? 0;
		const breakableFitAdvances = prepared.breakableFitAdvances[segmentIndex];
		if (breakableFitAdvances && breakableFitAdvances.length > 0) {
			let width = 0;
			for (let i = localStart; i < localEnd; i++) width += breakableFitAdvances[i] ?? 0;
			return width;
		}
		const fullWidth = prepared.widths[segmentIndex] ?? 0;
		if (segmentMeta.graphemeCount === 0) return fullWidth;
		return fullWidth * ((localEnd - localStart) / segmentMeta.graphemeCount);
	};
	const widthBetweenOffsets$1 = (prepared, segmentMetas, startOffset, endOffset) => {
		if (endOffset <= startOffset) return 0;
		let width = 0;
		for (let segmentIndex = 0; segmentIndex < segmentMetas.length; segmentIndex++) {
			const meta = segmentMetas[segmentIndex];
			if (endOffset <= meta.graphemeStart) break;
			if (startOffset >= meta.graphemeEnd) continue;
			const sliceStart = Math.max(startOffset, meta.graphemeStart);
			const sliceEnd = Math.min(endOffset, meta.graphemeEnd);
			width += getPartialSegmentWidth$1(prepared, segmentIndex, meta, sliceStart, sliceEnd);
		}
		return width;
	};
	const buildLineFragments = (prepared, segmentMetas, graphemes, layout, ranges) => {
		const lineViews = layout.lines.map((line) => {
			const lineStart = cursorToGlobalOffset$1(line.start, segmentMetas);
			const lineEnd = cursorToGlobalOffset$1(line.end, segmentMetas);
			return {
				line,
				lineStart,
				lineEnd,
				fragments: ranges.flatMap((range) => {
					if (range.end <= lineStart || range.start >= lineEnd) return [];
					const fragmentStart = Math.max(range.start, lineStart);
					const fragmentEnd = Math.min(range.end, lineEnd);
					return [{
						wordIndex: range.wordIndex,
						lineIndex: 0,
						word: range.word,
						text: graphemes.slice(fragmentStart, fragmentEnd).join(""),
						color: range.color,
						startX: widthBetweenOffsets$1(prepared, segmentMetas, lineStart, fragmentStart),
						endX: widthBetweenOffsets$1(prepared, segmentMetas, lineStart, fragmentEnd),
						fragmentStartInWord: fragmentStart - range.start,
						fragmentEndInWord: fragmentEnd - range.start,
						wordGraphemeCount: Math.max(range.end - range.start, 1),
						wordGraphemeTimings: range.graphemeTimings,
						fragmentIndexInWord: 0,
						fragmentCountInWord: 1,
						isPrimaryFragment: true,
						isSplitAcrossLines: false
					}];
				})
			};
		});
		const fragmentCountByWord = /* @__PURE__ */ new Map();
		lineViews.forEach((lineView) => {
			lineView.fragments.forEach((fragment) => {
				fragmentCountByWord.set(fragment.wordIndex, (fragmentCountByWord.get(fragment.wordIndex) ?? 0) + 1);
			});
		});
		const seenFragmentsByWord = /* @__PURE__ */ new Map();
		return lineViews.map((lineView, lineIndex) => ({
			...lineView,
			fragments: lineView.fragments.map((fragment) => {
				const fragmentCountInWord = fragmentCountByWord.get(fragment.wordIndex) ?? 1;
				const fragmentIndexInWord = seenFragmentsByWord.get(fragment.wordIndex) ?? 0;
				seenFragmentsByWord.set(fragment.wordIndex, fragmentIndexInWord + 1);
				return {
					...fragment,
					lineIndex,
					fragmentIndexInWord,
					fragmentCountInWord,
					isPrimaryFragment: fragmentIndexInWord === 0,
					isSplitAcrossLines: fragmentCountInWord > 1
				};
			})
		}));
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/cadenza/cadenzaPlacements.ts
	const buildEmphasisMap = (lineData, isInterlude) => {
		const emphasisMap = /* @__PURE__ */ new Map();
		if (isInterlude) return emphasisMap;
		const primaryFragments = lineData.flatMap((lineView) => lineView.fragments).filter((fragment) => fragment.isPrimaryFragment);
		const hero = primaryFragments.filter((fragment) => !fragment.isSplitAcrossLines && fragment.text.trim().length > 0).map((fragment) => {
			const graphemeCount = Math.max(fragment.wordGraphemeCount, splitGraphemes$2(fragment.word.text).length, 1);
			return {
				fragment,
				score: (isCJK$1(fragment.word.text) ? .18 : Math.min(graphemeCount * .08, .36)) + (1 - Math.abs(fragment.wordIndex - (primaryFragments.length - 1) / 2) / Math.max(primaryFragments.length, 1)) * .18
			};
		}).sort((a, b) => b.score - a.score)[0];
		if (hero) {
			const scoreBoost = 1 + clamp$9(hero.score - .48, 0, .52);
			emphasisMap.set(hero.fragment.wordIndex, 1.46 * scoreBoost);
		}
		return emphasisMap;
	};
	const buildWordPlacements = (lineData, fontPx, lineHeight, maxWidth, animationIntensity, seed, isInterlude) => {
		const totalHeight = Math.max(lineData.length, 1) * lineHeight;
		const baseMargin = animationIntensity === "calm" ? 4 : 6;
		const baseScale = animationIntensity === "chaotic" ? 1.02 : animationIntensity === "calm" ? 1 : 1.01;
		const emphasisMap = buildEmphasisMap(lineData, isInterlude);
		const heroWordIndex = [...emphasisMap.entries()].sort((a, b) => b[1] - a[1])[0]?.[0] ?? null;
		const placements = [];
		const occupiedRects = [];
		const occupiedBands = /* @__PURE__ */ new Map();
		const occupiedMarks = [];
		let occupiedQueryStamp = 0;
		const collisionBandSize = Math.max(24, Math.round(lineHeight * .9));
		const getBandIndex = (value) => Math.floor(value / collisionBandSize);
		const registerOccupiedRect = (rect) => {
			const rectIndex = occupiedRects.length;
			occupiedRects.push(rect);
			const startBand = getBandIndex(rect.top);
			const endBand = getBandIndex(rect.bottom);
			for (let band = startBand; band <= endBand; band++) {
				const bucket = occupiedBands.get(band);
				if (bucket) bucket.push(rectIndex);
				else occupiedBands.set(band, [rectIndex]);
			}
		};
		// Native planner: reuse scalar results and evaluate candidates lazily. The search
		// order and arithmetic stay identical without allocating arrays/objects per trial.
		let collisionOverlapArea = 0;
		const evaluateCollisionRect = (left, top, right, bottom) => {
			collisionOverlapArea = 0;
			if (occupiedRects.length === 0) return false;
			occupiedQueryStamp += 1;
			const stamp = occupiedQueryStamp;
			const startBand = getBandIndex(top);
			const endBand = getBandIndex(bottom);
			let intersects = false;
			let overlapArea = 0;
			for (let band = startBand; band <= endBand; band++) {
				const bucket = occupiedBands.get(band);
				if (!bucket) continue;
				for (let bucketIndex = 0; bucketIndex < bucket.length; bucketIndex++) {
					const rectIndex = bucket[bucketIndex];
					if (occupiedMarks[rectIndex] === stamp) continue;
					occupiedMarks[rectIndex] = stamp;
					const rect = occupiedRects[rectIndex];
					const overlapWidth = Math.max(0, Math.min(right, rect.right) - Math.max(left, rect.left));
					const overlapHeight = Math.max(0, Math.min(bottom, rect.bottom) - Math.max(top, rect.top));
					if (overlapWidth <= 0 || overlapHeight <= 0) continue;
					intersects = true;
					overlapArea += overlapWidth * overlapHeight;
				}
			}
			collisionOverlapArea = overlapArea;
			return intersects;
		};
		const pushPlacementRect = (left, top, width, height, padding) => {
			registerOccupiedRect({
				left: left - padding,
				top: top - height - padding,
				right: left + width + padding,
				bottom: top + padding
			});
		};
		const placementPlans = lineData.flatMap((lineView, lineIndex) => {
			const lineLeft = -lineView.line.width / 2;
			const baselineY = -totalHeight / 2 + fontPx + lineIndex * lineHeight;
			return lineView.fragments.map((fragment, fragmentIndex) => {
				const emphasis = emphasisMap.get(fragment.wordIndex) ?? 1;
				const width = Math.max(fragment.endX - fragment.startX, fontPx * .18);
				const scale = emphasis > 1 ? baseScale * emphasis : baseScale;
				const height = fontPx * scale * .95;
				return {
					fragment,
					lineIndex,
					fragmentIndex,
					baseX: lineLeft + fragment.startX,
					baseY: baselineY,
					emphasis,
					width,
					height,
					scale,
					collisionWidth: width * scale * (emphasis > 1 ? 1.48 : 1.26),
					collisionHeight: height * (emphasis > 1 ? 1.36 : 1.24),
					padding: baseMargin + (emphasis > 1 ? 10 : 2)
				};
			});
		}).sort((a, b) => {
			const emphasisDelta = b.emphasis - a.emphasis;
			if (Math.abs(emphasisDelta) > .001) return emphasisDelta;
			if (a.lineIndex !== b.lineIndex) return a.lineIndex - b.lineIndex;
			return a.fragment.startX - b.fragment.startX;
		});
		const primaryPlanByWordIndex = /* @__PURE__ */ new Map();
		placementPlans.forEach((plan) => {
			if (!primaryPlanByWordIndex.has(plan.fragment.wordIndex)) primaryPlanByWordIndex.set(plan.fragment.wordIndex, plan);
		});
		const heroPlan = heroWordIndex === null ? null : primaryPlanByWordIndex.get(heroWordIndex) ?? null;
		const heroMetrics = heroPlan ? {
			centerX: heroPlan.width * heroPlan.scale / 2,
			centerY: -heroPlan.height * .46,
			width: heroPlan.width * heroPlan.scale
		} : null;
		const nativeSearch = typeof nativeCadenzaSearch === "function" ? nativeCadenzaSearch : null;
		if (nativeSearch) nativeSearch(lineHeight);
		placementPlans.forEach((plan) => {
			const { fragment, lineIndex, fragmentIndex, emphasis, width, height, scale, collisionWidth, collisionHeight, padding } = plan;
			const wordSeed = seed + fragment.wordIndex * 17 + lineIndex * 31 + fragmentIndex * 13;
			const random = (offset) => {
				const x = Math.sin(wordSeed + offset) * 1e4;
				return x - Math.floor(x);
			};
			const rotate = 0;
			const passedRotate = (random(3) - .5) * (animationIntensity === "chaotic" ? 20 : 12);
			const entryOffsetX = 0;
			const entryOffsetY = 0;
			const step = Math.max(10, Math.round(fontPx * .14));
			const maxRadius = emphasis > 1 ? Math.max(20, lineHeight * .5) : Math.max(lineHeight * 2.2, collisionWidth * .75, 56);
			let preferredX = emphasis > 1 ? -width / 2 : plan.baseX;
			let preferredY = emphasis > 1 ? 0 : plan.baseY;
			if (!isInterlude && emphasis <= 1 && heroMetrics) {
				const wordCenterX = preferredX + width / 2;
				const wordCenterY = preferredY - height * .46;
				let dx = wordCenterX - heroMetrics.centerX;
				let dy = wordCenterY - heroMetrics.centerY;
				const distance = Math.hypot(dx, dy);
				if (distance < 1) {
					dx = preferredX >= 0 ? 1 : -1;
					dy = lineIndex % 2 === 0 ? -.65 : .65;
				}
				const minHeroSeparation = heroMetrics.width * .34 + width * .52 + padding * 2;
				if (distance < minHeroSeparation) {
					const normalizedDistance = Math.max(Math.hypot(dx, dy), 1);
					const ux = dx / normalizedDistance;
					const uy = dy / normalizedDistance;
					const push = minHeroSeparation - distance;
					preferredX += ux * push;
					preferredY += uy * push * .92;
				}
			}
			const horizontalMin = -maxWidth / 2 - 72;
			const horizontalMax = maxWidth / 2 + 72;
			const verticalMin = -Math.max(totalHeight * .9, lineHeight * 1.6);
			const verticalMax = Math.max(totalHeight * .9, lineHeight * 1.45);
			let chosenX = preferredX;
			let chosenY = preferredY;
			let found = false;
			let bestFallback = {
				x: preferredX,
				y: preferredY,
				score: Number.POSITIVE_INFINITY
			};
			const baseAngle = heroMetrics && emphasis <= 1 ? Math.atan2(preferredY, preferredX + width / 2) : 0;
			if (nativeSearch) {
				const position = nativeSearch(preferredX, preferredY, collisionWidth, collisionHeight, padding,
					step, maxRadius, emphasis, baseAngle, horizontalMin, horizontalMax, verticalMin, verticalMax);
				chosenX = position[0];
				chosenY = position[1];
				found = true;
			}
			for (let radius = 0; !nativeSearch && radius <= maxRadius && !found; radius += step) {
				const sampleCount = radius === 0 ? 1 : emphasis > 1 ? 8 : Math.max(12, Math.round(Math.PI * 2 * radius / Math.max(step * 1.1, 10)));
				for (let sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++) {
					const angle = baseAngle + sampleIndex / sampleCount * Math.PI * 2;
					const ellipseY = radius * (emphasis > 1 ? .8 : .92);
					const dx = radius === 0 ? 0 : Math.cos(angle) * radius;
					const dy = radius === 0 ? 0 : Math.sin(angle) * ellipseY;
					const left = preferredX + dx - padding;
					const top = preferredY + dy - collisionHeight - padding;
					const right = left + collisionWidth + padding * 2;
					const bottom = top + collisionHeight + padding * 2;
					if (!(left >= horizontalMin && right <= horizontalMax && top >= verticalMin && bottom <= verticalMax)) continue;
					const intersects = evaluateCollisionRect(left, top, right, bottom);
					const travel = Math.hypot(dx, dy);
					const score = collisionOverlapArea * 2.2 + travel;
					if (score < bestFallback.score) bestFallback = {
						x: preferredX + dx,
						y: preferredY + dy,
						score
					};
					if (!intersects) {
						chosenX = preferredX + dx;
						chosenY = preferredY + dy;
						pushPlacementRect(chosenX, chosenY, collisionWidth, collisionHeight, padding);
						found = true;
						break;
					}
				}
			}
			if (!found) {
				chosenX = bestFallback.x;
				chosenY = bestFallback.y;
				pushPlacementRect(chosenX, chosenY, collisionWidth, collisionHeight, padding);
			}
			const outwardX = chosenX + width / 2;
			const outwardY = chosenY - height * .46;
			const outwardLength = Math.max(Math.hypot(outwardX, outwardY), 1);
			const outwardUnitX = outwardX / outwardLength;
			const outwardUnitY = outwardY / outwardLength;
			const driftAmount = isInterlude ? 3 + random(6) * 3 : emphasis > 1 ? 4 + random(6) * 4 : animationIntensity === "chaotic" ? 8 + random(6) * 9 : 5 + random(6) * 6;
			const passedDriftX = outwardUnitX * driftAmount + (random(7) - .5) * 2.4;
			const passedDriftY = outwardUnitY * driftAmount * .72 + (random(8) - .5) * 2;
			placements.push({
				id: `${fragment.word.text}-${fragment.wordIndex}-${lineIndex}-${fragmentIndex}-${fragment.fragmentIndexInWord}`,
				wordIndex: fragment.wordIndex,
				word: fragment.word,
				text: fragment.text,
				color: fragment.color,
				x: chosenX,
				y: chosenY,
				width,
				height,
				rotate,
				scale,
				passedRotate,
				passedDriftX,
				passedDriftY,
				entryOffsetX,
				entryOffsetY,
				fragmentStartInWord: fragment.fragmentStartInWord,
				fragmentEndInWord: fragment.fragmentEndInWord,
				wordGraphemeCount: fragment.wordGraphemeCount,
				wordGraphemeTimings: fragment.wordGraphemeTimings,
				emphasis,
				isInterlude
			});
		});
		return placements;
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/cadenza/cadenzaPreparedState.ts
	const chooseFontPx = (width, line) => {
		const graphemeCount = splitGraphemes$2(line.fullText).length || 1;
		const wordCount = line.words.length || 1;
		const widthBase = clamp$9(width * .086, 34, 94);
		const lengthPenalty = graphemeCount > 12 ? Math.min((graphemeCount - 12) * 1.8, 34) : 0;
		const densityPenalty = wordCount > 7 ? Math.min((wordCount - 7) * 1.5, 18) : 0;
		return clamp$9(widthBase - lengthPenalty - densityPenalty, 28, 104);
	};
	const buildCanvasFont = (theme, fontPx) => `${resolveThemeFontWeight(theme, 700)} ${fontPx}px ${resolveThemeFontStack(theme)}`;
	const buildPreparedState = (line, context) => {
		const { showText, viewport, theme, tuning } = context;
		if (!showText || viewport.width <= 0 || viewport.height <= 0) return null;
		const fontPx = clamp$9(chooseFontPx(viewport.width, line) * tuning.fontScale, 24, 132);
		const font = buildCanvasFont(theme, fontPx);
		const prepared = prepareWithSegments(line.fullText, font);
		const text = prepared.segments.join("");
		const { segmentMetas, graphemes } = buildSegmentMetas$1(prepared);
		const lineHeight = Math.round(fontPx * (isCJK$1(text) ? 1.22 : 1.1));
		const availableWidth = Math.max(viewport.width - 48, 120);
		const minWidth = Math.min(220, availableWidth);
		const wrapCompression = graphemes.length > 12 ? clamp$9(.92 - (graphemes.length - 12) * .018, .62, .92) : .92;
		const compactWidthRatio = tuning.widthRatio * wrapCompression;
		const maxWidth = clamp$9(Math.min(viewport.width * compactWidthRatio, 820), minWidth, availableWidth);
		const layout = layoutWithLines(prepared, maxWidth, lineHeight);
		const ranges = findWordRanges(line, graphemes, theme);
		const lineFragments = buildLineFragments(prepared, segmentMetas, graphemes, layout, ranges);
		return {
			prepared,
			text,
			font,
			fontPx,
			lineHeight,
			maxWidth,
			layout,
			segmentMetas,
			graphemes,
			placements: buildWordPlacements(lineFragments, fontPx, lineHeight, maxWidth, theme.animationIntensity, line.startTime * 1e3, line.fullText === "......")
		};
	};

//#endregion
//#region ../../web/folia-major/src/types.ts
	const DEFAULT_CADENZA_TUNING = {
		fontScale: 1.12,
		widthRatio: .72,
		motionAmount: 1,
		glowIntensity: 1,
		beamIntensity: 0
	};
	const DEFAULT_PARTITA_TUNING = {
		showGuideLines: true,
		useSemanticLayout: true,
		staggerMin: 20,
		staggerMax: 100
	};
	const DEFAULT_DIORAMA_GEOMETRY_VISIBILITY = {
		enabled: true,
		mode: "clouds",
		strands: true,
		blobs: true,
		ribbons: true,
		rings: true
	};
	const DEFAULT_DIORAMA_TUNING = {
		cameraSpeed: 1,
		motionAmount: 1,
		audioReactivity: 1,
		geometryVisibility: DEFAULT_DIORAMA_GEOMETRY_VISIBILITY,
		particleDensity: 576,
		particleScale: 1,
		particleGlowEnabled: true,
		particleGlowIntensity: .65,
		showParticles: true,
		backgroundParticleCircumference: 28,
		backgroundParticleRadial: 2,
		glowEnabled: true,
		glowIntensity: 1,
		soulEnabled: true,
		soulIntensity: 1,
		soulActiveEnabled: false,
		gradientEnabled: false,
		gradientIntensity: 1,
		keywordColoringEnabled: true
	};

//#endregion
//#region \0words
	const getWordSegmentationKey = (line) => "";
	const hasWordSegmentationOverride = (line) => false;
	const segmentLyricWords = (line) => (line._segments ?? []).map((s) => ({
		segment: s.text,
		index: s.startOffset,
		isWordLike: s.isWordLike
	}));

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/lumiereStructure.ts
	const DEFAULT_LUMIERE_STRUCTURE_PARAMS = {
		gapMultiplier: 2.5,
		gapMin: 1.25,
		gapMax: 3.5,
		maxLines: 6,
		maxDuration: 18,
		recursiveSplit: true
	};
	const clamp$8 = (value, min, max) => Math.min(max, Math.max(min, value));
	const median$2 = (values) => {
		if (values.length === 0) return .5;
		const sorted = [...values].sort((a, b) => a - b);
		const middle = Math.floor(sorted.length / 2);
		return sorted.length % 2 === 0 ? ((sorted[middle - 1] ?? sorted[middle]) + sorted[middle]) / 2 : sorted[middle];
	};
	/** 编译用的行：视觉结束时间可以超出 endTime，但不越过下一行开始。 */
	const buildStructureLines = (lines) => lines.map((line, sourceIndex) => ({
		sourceIndex,
		line,
		renderEndTime: Math.max(line.startTime, Math.min(getLineRenderEndTime(line), lines[sourceIndex + 1]?.startTime ?? Number.POSITIVE_INFINITY))
	}));
	const resolveParagraphGapThreshold = (lines, params = DEFAULT_LUMIERE_STRUCTURE_PARAMS) => {
		const gaps = lines.slice(1).map((line, index) => line.startTime - Math.min(getLineRenderEndTime(lines[index]), line.startTime)).filter((gap) => gap > 0);
		return clamp$8(median$2(gaps) * params.gapMultiplier, params.gapMin, params.gapMax);
	};
	const metadataChanged$2 = (previous, next) => previous.blockIndex !== void 0 && next.blockIndex !== void 0 && previous.blockIndex !== next.blockIndex || previous.songPart !== void 0 && next.songPart !== void 0 && previous.songPart !== next.songPart;
	/** 超过行数或时长的段落在最大空隙处切开；recursiveSplit 时左半部分也继续切。 */
	const splitOversizedDraft$2 = (draft, params) => {
		const output = [];
		let remaining = draft.lines;
		let boundary = draft.boundary;
		let loopGuard = 0;
		while (remaining.length > params.maxLines || remaining.length > 1 && remaining.at(-1).renderEndTime - remaining[0].line.startTime > params.maxDuration) {
			if (loopGuard++ > 1e3) break;
			const rawSplitIndex = remaining.slice(2, -1).map((line, offset) => ({
				splitIndex: offset + 2,
				gap: line.line.startTime - remaining[offset + 1].renderEndTime
			})).filter((candidate) => !Number.isNaN(candidate.gap)).sort((a, b) => b.gap - a.gap)[0]?.splitIndex ?? Math.min(4, remaining.length - 1);
			const splitIndex = Math.max(1, rawSplitIndex);
			const head = {
				lines: remaining.slice(0, splitIndex),
				boundary
			};
			output.push(...params.recursiveSplit ? splitOversizedDraft$2(head, params) : [head]);
			remaining = remaining.slice(splitIndex);
			boundary = output.at(-1).lines.length >= params.maxLines ? "line-cap" : "duration-cap";
		}
		output.push({
			lines: remaining,
			boundary
		});
		return output;
	};
	/** 自动分段：元数据变化或空隙超过阈值处切开，再按上限切开超长的段落。 */
	const draftParagraphs = (lines, threshold, params = DEFAULT_LUMIERE_STRUCTURE_PARAMS) => {
		const drafts = [];
		let current = {
			lines: [],
			boundary: "song-start"
		};
		lines.forEach((line, index) => {
			const previous = lines[index - 1];
			const gap = previous ? line.line.startTime - previous.renderEndTime : 0;
			const boundary = previous && metadataChanged$2(previous.line, line.line) ? "metadata" : previous && gap >= threshold ? "time-gap" : null;
			if (boundary && current.lines.length > 0) {
				drafts.push(...splitOversizedDraft$2(current, params));
				current = {
					lines: [],
					boundary
				};
			}
			current.lines.push(line);
		});
		if (current.lines.length > 0) drafts.push(...splitOversizedDraft$2(current, params));
		return drafts;
	};
	/** 一行里「像词」的片段数。 */
	const countWordLike = (line) => segmentLyricWords(line).filter((part) => part.isWordLike).length;
	/** 段落性质：副歌标记 → 间奏标记 → 最后一段是尾声 → 短或词少是换气 → 标点多或词密是上扬 → 其余是主歌。 */
	const classifyParagraph$2 = (lines, index, total) => {
		if (lines.some((item) => item.line.isChorus || /chorus|副歌/i.test(item.line.songPart ?? ""))) return "chorus";
		if (lines.some((item) => /bridge|break|間奏|ブリッジ/i.test(item.line.songPart ?? ""))) return "break";
		if (index === total - 1) return "outro";
		const duration = lines.at(-1).renderEndTime - lines[0].line.startTime;
		const segmentCount = lines.reduce((sum, line) => sum + countWordLike(line.line), 0);
		const punctuationCount = lines.reduce((sum, line) => sum + (line.line.fullText.match(/[!?！？…]/g)?.length ?? 0), 0);
		if (duration <= 3.5 || segmentCount <= 3) return "breath";
		if (punctuationCount >= 2 || segmentCount / Math.max(duration, 1) > 2.5) return "lift";
		return "verse";
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/lumiereSeamless.ts
/** 把首尾相接的段落并成一个单元（少于两段时原样返回）。纯函数。 */
	const mergeLumiereParagraphs = (paragraphs) => {
		if (paragraphs.length < 2) return [...paragraphs];
		const first = paragraphs[0];
		const last = paragraphs.at(-1);
		const sections = paragraphs.map((paragraph) => ({
			startTime: paragraph.startTime,
			endTime: paragraph.endTime,
			kind: paragraph.kind
		}));
		return [{
			id: "lumiere-seamless",
			index: 0,
			kind: first.kind,
			boundary: first.boundary,
			startTime: first.startTime,
			endTime: last.endTime,
			lyricEndTime: last.lyricEndTime,
			lineIndices: paragraphs.flatMap((paragraph) => paragraph.lineIndices),
			lines: paragraphs.flatMap((paragraph) => paragraph.lines),
			shots: paragraphs.flatMap((paragraph) => paragraph.shots),
			transitionOut: null,
			opening: first.opening,
			sections
		}];
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/visualizerSeedHash.ts
/** FNV-1a 32 位字符串散列。 */
	const hashVisualizerSeed = (value) => {
		let hash = 2166136261;
		for (let index = 0; index < value.length; index += 1) {
			hash ^= value.charCodeAt(index);
			hash = Math.imul(hash, 16777619);
		}
		return hash >>> 0;
	};
	/** 把数字种子与盐混合，让不同子系统（版式、装饰、逐字抖动）互不相关。 */
	const mixVisualizerSeed = (seed, salt) => Math.imul((Math.trunc(seed) ^ salt) >>> 0, 2654435761) >>> 0;
	/** 按元素下标取确定性的 0..1 抖动。 */
	const visualizerHash01 = (seed, index, salt) => mixVisualizerSeed(seed + Math.imul(index + 1, 97), salt) / 4294967296;

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/lumiereRandom.ts
/** mulberry32：32 位状态，周期 2^32，对装饰用的随机足够。数字种子直接当状态，字符串种子先散列。 */
	const createRng = (seed) => {
		let state = typeof seed === "number" ? seed >>> 0 : hashVisualizerSeed(seed);
		return () => {
			state = state + 1831565813 | 0;
			let t = Math.imul(state ^ state >>> 15, 1 | state);
			t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t;
			return ((t ^ t >>> 14) >>> 0) / 4294967296;
		};
	};
	/**
	* 与 createRng(seed) 同一条流，但从第 skip 个值之后开始：mulberry32 每取一次状态加一个常数，跳过是 O(1)。
	* 歌词窗口按需构建某一行时用它直接跳到这一行的起点，拿到的值与从头顺序取完全一样。
	*/
	const createRngAt = (seed, skip) => {
		const state = typeof seed === "number" ? seed >>> 0 : hashVisualizerSeed(seed);
		return createRng(state + Math.imul(skip, 1831565813) >>> 0);
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/lineart/recipes.ts
	const lumiereScaleMask$7 = globalThis.devicePixelRatio | 0;
	const LUMIERE_NEUTRAL_OFFSET$7 = (523 ^ lumiereScaleMask$7) + Math.imul(47868 ^ lumiereScaleMask$7, 15875 ^ lumiereScaleMask$7) - ((523 ^ lumiereScaleMask$7) + Math.imul(47868 ^ lumiereScaleMask$7, 15875 ^ lumiereScaleMask$7));
	const arcPoints = (cx, cy, r, from, to, steps = 64 + LUMIERE_NEUTRAL_OFFSET$7) => Array.from({ length: steps + 1 }, (_, i) => {
		const a = from + (to - from) * i / steps;
		return [cx + Math.cos(a) * r, cy + Math.sin(a) * r];
	});
	const cubicPoints = (p0, p1, p2, p3, steps = 40) => Array.from({ length: steps + 1 }, (_, i) => {
		const t = i / steps;
		const u = 1 - t;
		const a = u * u * u, b = 3 * u * u * t, c = 3 * u * t * t, d = t * t * t;
		return [a * p0[0] + b * p1[0] + c * p2[0] + d * p3[0], a * p0[1] + b * p1[1] + c * p2[1] + d * p3[1]];
	});
	const line = (a, b) => [a, b];
	const mergeSpecs = (...specs) => ({
		paths: specs.flatMap((spec) => spec.paths),
		nodes: specs.flatMap((spec) => spec.nodes)
	});
	/**
	* 量角器似的光环：以光源为圆心的下半圆（外弧 + 内弧），辐条，外弧上的细刻度。参考图顶部那一圈。
	*/
	const protractorHalo = (options) => {
		const { cx, cy, radius } = options;
		const delay = options.delay ?? 0;
		const alpha = options.alpha ?? .5;
		const from = Math.PI * .04;
		const to = Math.PI * .96;
		const inner = radius * .62;
		const paths = [
			{
				points: arcPoints(cx, cy, radius, to, from, 96),
				width: .0016,
				alpha,
				delay,
				span: .45
			},
			{
				points: arcPoints(cx, cy, inner, from, to, 72),
				width: .0013,
				alpha: alpha * .8,
				delay: delay + .08,
				span: .4
			},
			{
				points: arcPoints(cx, cy, radius * 1.18, from + .1, to - .1, 80),
				width: .0011,
				alpha: alpha * .45,
				delay: delay + .2,
				span: .4,
				dash: [.004, .009]
			}
		];
		const step = options.spokeStep ?? Math.PI / 12;
		const nodes = [];
		let index = 0;
		for (let a = Math.PI / 2 - step * 5; a <= Math.PI / 2 + step * 5 + 1e-6; a += step) {
			const long = index % 2 === 0;
			const r0 = inner * .2;
			const r1 = radius * (long ? 1.12 : 1.02);
			paths.push({
				points: line([cx + Math.cos(a) * r0, cy + Math.sin(a) * r0], [cx + Math.cos(a) * r1, cy + Math.sin(a) * r1]),
				width: long ? .0013 : 9e-4,
				alpha: alpha * (long ? .7 : .45),
				delay: delay + .12 + Math.abs(a - Math.PI / 2) * .12,
				span: .3
			});
			if (long) nodes.push({
				at: [cx + Math.cos(a) * radius, cy + Math.sin(a) * radius],
				size: .022,
				delay: delay + .45,
				twinklePhase: index * 1.7
			});
			index += 1;
		}
		for (let a = from; a <= to; a += Math.PI / 72) paths.push({
			points: line([cx + Math.cos(a) * radius, cy + Math.sin(a) * radius], [cx + Math.cos(a) * radius * .975, cy + Math.sin(a) * radius * .975]),
			width: 8e-4,
			alpha: alpha * .4,
			delay: delay + .25 + (a - from) * .05,
			span: .15
		});
		return {
			paths,
			nodes
		};
	};
	/** 取景框：一个大矩形、四角加粗的角标、边上的刻度、两侧竖直的点线。 */
	const viewfinderFrame = (options) => {
		const { aspect, top, bottom } = options;
		const left = options.left * aspect;
		const right = options.right * aspect;
		const delay = options.delay ?? 0;
		const alpha = options.alpha ?? .3;
		const corner = .03;
		const paths = [{
			points: [
				[left, top],
				[right, top],
				[right, bottom],
				[left, bottom],
				[left, top]
			],
			width: 9e-4,
			alpha: alpha * .55,
			delay,
			span: .6
		}];
		[
			[
				[left, top + corner],
				[left, top],
				[left + corner, top]
			],
			[
				[right - corner, top],
				[right, top],
				[right, top + corner]
			],
			[
				[right, bottom - corner],
				[right, bottom],
				[right - corner, bottom]
			],
			[
				[left + corner, bottom],
				[left, bottom],
				[left, bottom - corner]
			]
		].forEach((points, i) => paths.push({
			points,
			width: .0018,
			alpha,
			delay: delay + .1 + i * .05,
			span: .2
		}));
		const ticks = 12;
		for (let i = 1; i < ticks; i += 1) {
			const x = left + (right - left) * i / ticks;
			const y = top + (bottom - top) * i / ticks;
			const len = i % 3 === 0 ? .014 : .007;
			paths.push({
				points: line([x, top], [x, top + len]),
				width: 8e-4,
				alpha: alpha * .6,
				delay: delay + .3 + i * .01,
				span: .1
			});
			paths.push({
				points: line([x, bottom], [x, bottom - len]),
				width: 8e-4,
				alpha: alpha * .6,
				delay: delay + .3 + i * .01,
				span: .1
			});
			paths.push({
				points: line([left, y], [left + len, y]),
				width: 8e-4,
				alpha: alpha * .6,
				delay: delay + .3 + i * .01,
				span: .1
			});
			paths.push({
				points: line([right, y], [right - len, y]),
				width: 8e-4,
				alpha: alpha * .6,
				delay: delay + .3 + i * .01,
				span: .1
			});
		}
		[left + (right - left) * .14, right - (right - left) * .14].forEach((x, i) => paths.push({
			points: line([x, top + .06], [x, bottom - .06]),
			width: .001,
			alpha: alpha * .5,
			delay: delay + .35 + i * .05,
			span: .4,
			dash: [.003, .012]
		}));
		return {
			paths,
			nodes: []
		};
	};
	/**
	* 萌芽：从画面底部升起的茎，顶端两片叶（外轮廓 + 主脉 + 侧脉），叶尖与叶缘有闪点。参考图底部的两片叶。
	* baseY 为茎顶（叶的起点），size 为叶长。
	*/
	const sprout = (options) => {
		const { cx, baseY, size } = options;
		const delay = options.delay ?? 0;
		const alpha = options.alpha ?? .55;
		const open = options.open ?? 1;
		const paths = [{
			points: cubicPoints([cx, 1.04], [cx + .01, .94], [cx - .008, baseY + .08], [cx, baseY]),
			width: .0016,
			alpha,
			delay,
			span: .3
		}];
		const nodes = [];
		const leaf = (side, index) => {
			const lean = (.2 + .2 * open) * Math.PI;
			const d = [Math.sin(lean) * side, -Math.cos(lean)];
			const n = [-d[1], d[0]];
			const base = [cx, baseY];
			const tip = [base[0] + d[0] * size, base[1] + d[1] * size - size * .12];
			const bulge = size * .34;
			const at = (along, across) => [base[0] + d[0] * size * along + n[0] * across, base[1] + d[1] * size * along + n[1] * across - size * .12 * along * along];
			const leafDelay = delay + .22 + index * .08;
			paths.push({
				points: cubicPoints(base, at(.3, bulge), at(.78, bulge * .9), tip, 48),
				width: .0026,
				alpha,
				delay: leafDelay,
				span: .35
			});
			paths.push({
				points: cubicPoints(base, at(.3, -bulge * .95), at(.8, -bulge * .6), tip, 48),
				width: .0026,
				alpha,
				delay: leafDelay + .04,
				span: .35
			});
			paths.push({
				points: cubicPoints(base, at(.35, bulge * .08), at(.7, bulge * .05), tip, 32),
				width: .0011,
				alpha: alpha * .7,
				delay: leafDelay + .12,
				span: .3
			});
			for (let v = 1; v <= 4; v += 1) {
				const f = v / 5.2;
				const start = at(f, bulge * .04);
				for (const s of [1, -1]) {
					const end = at(f + .14, s * bulge * (.72 - f * .35));
					paths.push({
						points: cubicPoints(start, at(f + .04, s * bulge * .25), at(f + .1, s * bulge * .5), end, 16),
						width: 8e-4,
						alpha: alpha * .45,
						delay: leafDelay + .2 + v * .03,
						span: .18
					});
				}
			}
			nodes.push({
				at: tip,
				size: .03,
				delay: leafDelay + .3,
				twinklePhase: index * 2.1
			});
			nodes.push({
				at: at(.45, bulge * .86),
				size: .014,
				delay: leafDelay + .33,
				twinklePhase: index * 2.1 + 1
			});
			nodes.push({
				at: at(.6, -bulge * .8),
				size: .012,
				delay: leafDelay + .36,
				twinklePhase: index * 2.1 + 2
			});
		};
		leaf(-1, 0);
		leaf(1, 1);
		nodes.push({
			at: [cx, baseY],
			size: .02,
			delay: delay + .3,
			twinklePhase: .5
		});
		return {
			paths,
			nodes
		};
	};
	/** 散落的小闪点（参考图里画面各处零星的亮点）。 */
	const scatteredSparks = (options) => {
		const [x0, y0, x1, y1] = options.region ?? [
			.05,
			.08,
			.95,
			.92
		];
		return {
			paths: [],
			nodes: Array.from({ length: options.count }, (_, i) => ({
				at: [(x0 + (x1 - x0) * options.random()) * options.aspect, y0 + (y1 - y0) * options.random()],
				size: .006 + options.random() * .012,
				delay: (options.delay ?? 0) + options.random() * .5,
				twinklePhase: i * 2.399
			}))
		};
	};
	/** 百叶窗：窗框 + 一排横向叶片（窗隙族）。x、y、w、h 为高度单位。 */
	const blindsWindow = (options) => {
		const { x, y, w, h, slats } = options;
		const delay = options.delay ?? 0;
		const alpha = options.alpha ?? .5;
		const paths = [{
			points: [
				[x, y],
				[x + w, y],
				[x + w, y + h],
				[x, y + h],
				[x, y]
			],
			width: .0018,
			alpha,
			delay,
			span: .5
		}];
		for (let i = 1; i < slats; i += 1) {
			const sy = y + h * i / slats;
			paths.push({
				points: line([x, sy], [x + w, sy]),
				width: .001,
				alpha: alpha * .6,
				delay: delay + .1 + i * .03,
				span: .2
			});
		}
		return {
			paths,
			nodes: [{
				at: [x + w, y + h],
				size: .018,
				delay: delay + .4,
				twinklePhase: 1
			}]
		};
	};
	/** 三棱镜：正三角形轮廓（棱镜族），cx、cy 为中心，size 为边长（高度单位）。 */
	const prismTriangle = (options) => {
		const { cx, cy, size } = options;
		const h = size * Math.sqrt(3) / 2;
		const top = [cx, cy - h * 2 / 3];
		const left = [cx - size / 2, cy + h / 3];
		const right = [cx + size / 2, cy + h / 3];
		const alpha = options.alpha ?? .7;
		const delay = options.delay ?? 0;
		return {
			paths: [{
				points: [
					top,
					right,
					left,
					top
				],
				width: .0022,
				alpha,
				delay,
				span: .5
			}, {
				points: [top, [cx, cy + h / 3]],
				width: 8e-4,
				alpha: alpha * .4,
				delay: delay + .3,
				span: .3,
				dash: [.004, .008]
			}],
			nodes: [
				top,
				left,
				right
			].map((at, i) => ({
				at,
				size: .02,
				delay: delay + .4 + i * .05,
				twinklePhase: i * 1.3
			}))
		};
	};
	/** 双缝挡板：一条横线中间开两个缝（衍射族）。y 为挡板高度，gap 为缝宽，separation 为两缝间距（高度单位）。 */
	const slitBarrier = (options) => {
		const { cx, y, width, gap, separation } = options;
		const alpha = options.alpha ?? .6;
		const delay = options.delay ?? 0;
		const a = cx - separation / 2;
		const b = cx + separation / 2;
		return {
			paths: [
				[cx - width / 2, a - gap / 2],
				[a + gap / 2, b - gap / 2],
				[b + gap / 2, cx + width / 2]
			].map(([from, to], i) => ({
				points: line([from, y], [to, y]),
				width: .0026,
				alpha,
				delay: delay + i * .08,
				span: .3
			})),
			nodes: [a, b].map((x, i) => ({
				at: [x, y],
				size: .022,
				delay: delay + .35 + i * .05,
				twinklePhase: i * 2
			}))
		};
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/lineart/diagrams.ts
	const lumiereScaleMask$6 = globalThis.devicePixelRatio | 0;
	const LUMIERE_NEUTRAL_OFFSET$6 = (15230 ^ lumiereScaleMask$6) + Math.imul(10553 ^ lumiereScaleMask$6, 3118931976 ^ lumiereScaleMask$6) - ((15230 ^ lumiereScaleMask$6) + Math.imul(10553 ^ lumiereScaleMask$6, 3118931976 ^ lumiereScaleMask$6));
	const TAU$2 = Math.PI * (2 + LUMIERE_NEUTRAL_OFFSET$6);
	const path = (points, style = {}) => ({
		points,
		width: style.width ?? .0016,
		alpha: style.alpha ?? .55,
		delay: style.delay ?? 0,
		span: style.span ?? .4,
		...style.dash ? { dash: style.dash } : {}
	});
	const node = (at, size = .018, delay = .4, twinklePhase = 0) => ({
		at,
		size,
		delay,
		twinklePhase
	});
	const segment = (a, b, style = {}) => path([a, b], style);
	/** 圆 / 椭圆（可旋转）。 */
	const ellipsePath = (cx, cy, rx, ry, rotation = 0, from = 0, to = TAU$2, steps = 96) => {
		const cos = Math.cos(rotation);
		const sin = Math.sin(rotation);
		return arcPoints(0, 0, 1, from, to, steps).map(([x, y]) => [cx + x * rx * cos - y * ry * sin, cy + x * rx * sin + y * ry * cos]);
	};
	const circle = (cx, cy, r, style = {}) => path(ellipsePath(cx, cy, r, r), style);
	const merge = (...specs) => ({
		paths: specs.flatMap((spec) => spec.paths),
		nodes: specs.flatMap((spec) => spec.nodes)
	});
	/** 放射线：从 r0 到 r1、在 from..to 角度之间均匀 count 条。 */
	const rays = (cx, cy, r0, r1, count, from = 0, to = TAU$2, style = {}) => ({
		paths: Array.from({ length: count }, (_, i) => {
			const a = from + (to - from) * (i + (to - from >= TAU$2 - 1e-6 ? 0 : .5)) / count;
			return segment([cx + Math.cos(a) * r0, cy + Math.sin(a) * r0], [cx + Math.cos(a) * r1, cy + Math.sin(a) * r1], {
				width: .001,
				alpha: .4,
				...style,
				delay: (style.delay ?? 0) + i / count * .3,
				span: style.span ?? .2
			});
		}),
		nodes: []
	});
	const concentric = (cx, cy, radii, style = {}) => ({
		paths: radii.map((r, i) => circle(cx, cy, r, {
			width: .0011,
			alpha: .45,
			...style,
			delay: (style.delay ?? 0) + i * .06
		})),
		nodes: []
	});
	/** 十字窗：窗框 + 十字窗棂。 */
	const crossWindow = (x, y, w, h, style = {}) => ({
		paths: [
			path([
				[x, y],
				[x + w, y],
				[x + w, y + h],
				[x, y + h],
				[x, y]
			], {
				width: .002,
				alpha: .55,
				...style
			}),
			segment([x + w / 2, y], [x + w / 2, y + h], {
				width: .0014,
				alpha: .45,
				delay: (style.delay ?? 0) + .15
			}),
			segment([x, y + h / 2], [x + w, y + h / 2], {
				width: .0014,
				alpha: .45,
				delay: (style.delay ?? 0) + .2
			})
		],
		nodes: [node([x + w / 2, y + h / 2], .02, (style.delay ?? 0) + .4)]
	});
	/** 拱窗：矩形窗身 + 半圆拱顶。 */
	const archWindow$1 = (x, y, w, h, style = {}) => {
		const r = w / 2;
		const top = y + r;
		return {
			paths: [path([
				[x, y + h],
				[x, top],
				...arcPoints(x + r, top, r, Math.PI, TAU$2, 40),
				[x + w, y + h],
				[x, y + h]
			], {
				width: .0018,
				alpha: .55,
				...style
			}), segment([x + r, y], [x + r, y + h], {
				width: .001,
				alpha: .35,
				delay: (style.delay ?? 0) + .2
			})],
			nodes: [node([x + r, y + .01], .018, (style.delay ?? 0) + .4)]
		};
	};
	/** 玫瑰窗：外圈、内圈、辐条与花瓣。 */
	const roseWindow = (cx, cy, r, style = {}) => merge(concentric(cx, cy, [
		r,
		r * .62,
		r * .22
	], {
		width: .0016,
		alpha: .55,
		...style
	}), rays(cx, cy, r * .22, r, 12, 0, TAU$2, { delay: (style.delay ?? 0) + .2 }), {
		paths: Array.from({ length: 12 }, (_, i) => {
			const a = i / 12 * TAU$2;
			return circle(cx + Math.cos(a) * r * .8, cy + Math.sin(a) * r * .8, r * .14, {
				width: 9e-4,
				alpha: .35,
				delay: (style.delay ?? 0) + .35 + i * .02
			});
		}),
		nodes: [node([cx, cy], .028, (style.delay ?? 0) + .5)]
	});
	/** 门缝：两扇门的边线，中间一道缝。 */
	const doorSlit = (cx, top, bottom, gap, style = {}) => ({
		paths: [
			segment([cx - gap / 2, top], [cx - gap / 2, bottom], {
				width: .0018,
				alpha: .55,
				...style
			}),
			segment([cx + gap / 2, top], [cx + gap / 2, bottom], {
				width: .0018,
				alpha: .55,
				...style,
				delay: (style.delay ?? 0) + .05
			}),
			segment([cx - gap / 2 - .25, bottom], [cx + gap / 2 + .25, bottom], {
				width: .001,
				alpha: .35,
				delay: (style.delay ?? 0) + .2
			})
		],
		nodes: [node([cx, top + .02], .02, (style.delay ?? 0) + .4)]
	});
	/** 方格：障子、格栅（diagonal 为斜向菱格）。 */
	const gridPanel = (x, y, w, h, cols, rows, diagonal = false, style = {}) => {
		const paths = [path([
			[x, y],
			[x + w, y],
			[x + w, y + h],
			[x, y + h],
			[x, y]
		], {
			width: .0018,
			alpha: .5,
			...style
		})];
		const inner = {
			width: 9e-4,
			alpha: .32,
			delay: (style.delay ?? 0) + .15,
			span: .25
		};
		if (diagonal) {
			const n = cols + rows;
			for (let i = 1; i < n; i += 1) {
				const t = i / n;
				paths.push(segment([x + w * Math.min(1, t * 2), y + h * Math.max(0, t * 2 - 1)], [x + w * Math.max(0, t * 2 - 1), y + h * Math.min(1, t * 2)], inner));
				paths.push(segment([x + w * (1 - Math.min(1, t * 2)), y + h * Math.max(0, t * 2 - 1)], [x + w * (1 - Math.max(0, t * 2 - 1)), y + h * Math.min(1, t * 2)], inner));
			}
		} else {
			for (let i = 1; i < cols; i += 1) paths.push(segment([x + w * i / cols, y], [x + w * i / cols, y + h], inner));
			for (let j = 1; j < rows; j += 1) paths.push(segment([x, y + h * j / rows], [x + w, y + h * j / rows], inner));
		}
		return {
			paths,
			nodes: []
		};
	};
	/** 殿堂：一排高窗。 */
	const tallWindows = (x0, x1, y, h, count, style = {}) => merge(...Array.from({ length: count }, (_, i) => {
		const w = (x1 - x0) / count * .45;
		const x = x0 + (x1 - x0) * (i + .5) / count - w / 2;
		return archWindow$1(x, y, w, h, {
			alpha: .45,
			...style,
			delay: (style.delay ?? 0) + i * .07
		});
	}));
	/** 碎晶：散落的小三角晶片。 */
	const shards = (aspect, random, count, style = {}) => ({
		paths: Array.from({ length: count }, (_, i) => {
			const cx = (.1 + random() * .8) * aspect;
			const cy = .12 + random() * .76;
			const r = .025 + random() * .05;
			const a = random() * TAU$2;
			const points = [
				0,
				1,
				2,
				0
			].map((k) => [cx + Math.cos(a + k * TAU$2 / 3) * r, cy + Math.sin(a + k * TAU$2 / 3) * r * (.7 + random() * .6)]);
			return path(points, {
				width: .0014,
				alpha: .5,
				...style,
				delay: (style.delay ?? 0) + i * .04,
				span: .25
			});
		}),
		nodes: []
	});
	/** 发射谱线：一排长短不一的竖线（下面一条刻度基线）。 */
	const spectrumLines = (x0, x1, y, h, random, count, style = {}) => ({
		paths: [segment([x0, y + h / 2], [x1, y + h / 2], {
			width: .001,
			alpha: .35,
			...style
		}), ...Array.from({ length: count }, (_, i) => {
			const x = x0 + (x1 - x0) * (.05 + .9 * random());
			const k = .3 + random() * .7;
			return segment([x, y + h / 2], [x, y + h / 2 - h * k], {
				width: .0012 + random() * .0012,
				alpha: .4 + k * .3,
				delay: (style.delay ?? 0) + .1 + i * .03,
				span: .2
			});
		})],
		nodes: []
	});
	/** 立方分光镜：正方形 + 对角的分光面。 */
	const cubeSplitter = (cx, cy, size, style = {}) => ({
		paths: [path([
			[cx - size / 2, cy - size / 2],
			[cx + size / 2, cy - size / 2],
			[cx + size / 2, cy + size / 2],
			[cx - size / 2, cy + size / 2],
			[cx - size / 2, cy - size / 2]
		], {
			width: .002,
			alpha: .6,
			...style
		}), segment([cx - size / 2, cy + size / 2], [cx + size / 2, cy - size / 2], {
			width: .0012,
			alpha: .45,
			delay: (style.delay ?? 0) + .2
		})],
		nodes: [node([cx, cy], .022, (style.delay ?? 0) + .35)]
	});
	/** 虹与霓：两道同心弧。 */
	const rainbowArcs = (cx, cy, r, style = {}) => ({
		paths: [
			path(arcPoints(cx, cy, r, Math.PI * 1.08, Math.PI * 1.92, 72), {
				width: .0018,
				alpha: .5,
				...style
			}),
			path(arcPoints(cx, cy, r * 1.18, Math.PI * 1.1, Math.PI * 1.9, 72), {
				width: .001,
				alpha: .3,
				delay: (style.delay ?? 0) + .15,
				dash: [.006, .01]
			}),
			circle(cx, cy - r * .4, .03, {
				width: .0012,
				alpha: .45,
				delay: (style.delay ?? 0) + .3
			})
		],
		nodes: []
	});
	/** 玻璃杯：杯口椭圆 + 杯身。 */
	const glassCup = (cx, top, w, h, style = {}) => ({
		paths: [
			path(ellipsePath(cx, top, w / 2, w * .12), {
				width: .0016,
				alpha: .5,
				...style
			}),
			path([
				[cx - w / 2, top],
				[cx - w * .4, top + h],
				[cx + w * .4, top + h],
				[cx + w / 2, top]
			], {
				width: .0016,
				alpha: .5,
				delay: (style.delay ?? 0) + .1
			}),
			path(ellipsePath(cx, top + h, w * .4, w * .09, 0, 0, Math.PI), {
				width: .001,
				alpha: .35,
				delay: (style.delay ?? 0) + .2
			})
		],
		nodes: [node([cx - w * .3, top + h * .3], .016, (style.delay ?? 0) + .35)]
	});
	/** 涟漪：同心圆（扁）。 */
	const ripples = (cx, cy, r, count, style = {}) => ({
		paths: Array.from({ length: count }, (_, i) => path(ellipsePath(cx, cy, r * (i + 1) / count, r * .3 * (i + 1) / count), {
			width: .0012,
			alpha: .5 - i * .07,
			...style,
			delay: (style.delay ?? 0) + i * .08
		})),
		nodes: [node([cx, cy], .02, (style.delay ?? 0) + .3)]
	});
	/** 水面：一条起伏的线（从下往上看的水面）。 */
	const waterline = (aspect, y, amplitude, waves, style = {}) => ({
		paths: [path(Array.from({ length: 121 }, (_, i) => {
			return [i / 120 * aspect, y + Math.sin(i / 120 * waves * TAU$2) * amplitude];
		}), {
			width: .0014,
			alpha: .45,
			...style,
			span: .6
		})],
		nodes: []
	});
	/** 放大镜：镜片圆 + 手柄。 */
	const magnifier = (cx, cy, r, style = {}) => ({
		paths: [
			circle(cx, cy, r, {
				width: .0022,
				alpha: .6,
				...style
			}),
			circle(cx, cy, r * .9, {
				width: 9e-4,
				alpha: .3,
				delay: (style.delay ?? 0) + .1
			}),
			segment([cx + r * .7, cy + r * .7], [cx + r * 1.6, cy + r * 1.6], {
				width: .004,
				alpha: .5,
				delay: (style.delay ?? 0) + .2
			})
		],
		nodes: []
	});
	/** 水晶：六边形晶面与内部的刻面线。 */
	const crystal = (cx, cy, r, style = {}) => {
		const outer = Array.from({ length: 7 }, (_, i) => [cx + Math.cos(i * TAU$2 / 6) * r, cy + Math.sin(i * TAU$2 / 6) * r * 1.3]);
		return {
			paths: [path(outer, {
				width: .002,
				alpha: .55,
				...style
			}), ...[
				0,
				1,
				2
			].map((i) => segment(outer[i], outer[i + 3], {
				width: 9e-4,
				alpha: .3,
				delay: (style.delay ?? 0) + .15 + i * .05
			}))],
			nodes: outer.slice(0, 6).filter((_, i) => i % 2 === 0).map((at, i) => node(at, .016, (style.delay ?? 0) + .4, i))
		};
	};
	/** 透镜：双凸（convex）或双凹（concave）的轮廓 + 贯穿的光轴虚线。 */
	const lens = (cx, cy, h, convex, aspect, style = {}) => {
		const bulge = h * .18;
		const top = [cx, cy - h / 2];
		const bottom = [cx, cy + h / 2];
		const side = (sign) => cubicPoints(top, [cx + sign * (convex ? bulge : -bulge * .2) + (convex ? 0 : sign * bulge * .6), cy - h / 4], [cx + sign * (convex ? bulge : -bulge * .2) + (convex ? 0 : sign * bulge * .6), cy + h / 4], bottom, 32);
		const outline = convex ? [...side(1), ...side(-1).reverse()] : [
			[cx - bulge * .7, cy - h / 2],
			[cx + bulge * .7, cy - h / 2],
			...cubicPoints([cx + bulge * .7, cy - h / 2], [cx + bulge * .1, cy - h / 4], [cx + bulge * .1, cy + h / 4], [cx + bulge * .7, cy + h / 2], 24),
			[cx - bulge * .7, cy + h / 2],
			...cubicPoints([cx - bulge * .7, cy + h / 2], [cx - bulge * .1, cy + h / 4], [cx - bulge * .1, cy - h / 4], [cx - bulge * .7, cy - h / 2], 24)
		];
		return {
			paths: [path(outline, {
				width: .002,
				alpha: .6,
				...style
			}), segment([aspect * .05, cy], [aspect * .95, cy], {
				width: 9e-4,
				alpha: .3,
				delay: (style.delay ?? 0) + .1,
				dash: [.008, .008],
				span: .5
			})],
			nodes: []
		};
	};
	/** 一条光线（折线），终点有闪点。 */
	const rayPath = (points, style = {}) => ({
		paths: [path(points, {
			width: .0014,
			alpha: .6,
			span: .5,
			...style
		})],
		nodes: [node(points[points.length - 1], .016, (style.delay ?? 0) + (style.span ?? .5), points.length)]
	});
	/** 焦点标记：小十字 + 标签位置的短横。 */
	const focusMark = (x, y, style = {}) => ({
		paths: [segment([x - .012, y], [x + .012, y], {
			width: .0012,
			alpha: .55,
			...style
		}), segment([x, y - .012], [x, y + .012], {
			width: .0012,
			alpha: .55,
			...style
		})],
		nodes: [node([x, y], .024, (style.delay ?? 0) + .3)]
	});
	/** 平面镜：一条粗线、背面的斜线阴影、法线虚线与入射 / 反射角的小弧。 */
	const mirror = (cx, cy, length, angle, style = {}) => {
		const dx = Math.cos(angle) * length / 2;
		const dy = Math.sin(angle) * length / 2;
		const nx = -Math.sin(angle);
		const ny = Math.cos(angle);
		const hatches = Array.from({ length: 10 }, (_, i) => {
			const t = -.45 + i / 9 * .9;
			const bx = cx + dx * 2 * t;
			const by = cy + dy * 2 * t;
			return segment([bx, by], [bx + nx * .015 - dx * .04, by + ny * .015 - dy * .04], {
				width: 8e-4,
				alpha: .3,
				delay: (style.delay ?? 0) + .2 + i * .01,
				span: .1
			});
		});
		return {
			paths: [
				segment([cx - dx, cy - dy], [cx + dx, cy + dy], {
					width: .003,
					alpha: .6,
					...style
				}),
				...hatches,
				segment([cx, cy], [cx - nx * length * .5, cy - ny * length * .5], {
					width: 9e-4,
					alpha: .35,
					delay: (style.delay ?? 0) + .25,
					dash: [.006, .006]
				}),
				path(arcPoints(cx, cy, length * .12, Math.atan2(-ny, -nx) - .5, Math.atan2(-ny, -nx) + .5, 20), {
					width: 9e-4,
					alpha: .4,
					delay: (style.delay ?? 0) + .35
				})
			],
			nodes: []
		};
	};
	/** 介质界面：一条横线，下半部分用细斜线表示另一种介质。 */
	const interfaceLine = (aspect, y, style = {}) => ({
		paths: [segment([aspect * .06, y], [aspect * .94, y], {
			width: .002,
			alpha: .55,
			...style,
			span: .5
		}), ...Array.from({ length: 24 }, (_, i) => {
			const x = aspect * (.08 + i / 23 * .84);
			return segment([x, y + .01], [x - .02, y + .04], {
				width: 8e-4,
				alpha: .22,
				delay: (style.delay ?? 0) + .2 + i * .01,
				span: .1
			});
		})],
		nodes: []
	});
	/** 光具座：一条长刻度尺。 */
	const ruler = (x0, x1, y, ticks, style = {}) => ({
		paths: [segment([x0, y], [x1, y], {
			width: .0016,
			alpha: .5,
			...style,
			span: .5
		}), ...Array.from({ length: ticks + 1 }, (_, i) => {
			const x = x0 + (x1 - x0) * i / ticks;
			const long = i % 5 === 0;
			return segment([x, y], [x, y + (long ? .02 : .01)], {
				width: 8e-4,
				alpha: .4,
				delay: (style.delay ?? 0) + .2 + i * .005,
				span: .08
			});
		})],
		nodes: []
	});
	/** 光纤：一条弯曲的双线管道。 */
	const fiber = (points, thickness, style = {}) => {
		const center = cubicPoints(...points, 60);
		const offset = (sign) => center.map(([x, y], i) => {
			const [nx0, ny0] = center[Math.max(0, i - 1)];
			const [nx1, ny1] = center[Math.min(center.length - 1, i + 1)];
			const dx = nx1 - nx0;
			const dy = ny1 - ny0;
			const len = Math.hypot(dx, dy) || 1;
			return [x - dy / len * thickness * sign, y + dx / len * thickness * sign];
		});
		return {
			paths: [path(offset(1), {
				width: .0014,
				alpha: .5,
				...style,
				span: .6
			}), path(offset(-1), {
				width: .0014,
				alpha: .5,
				...style,
				delay: (style.delay ?? 0) + .05,
				span: .6
			})],
			nodes: [node(center[center.length - 1], .024, (style.delay ?? 0) + .6)]
		};
	};
	/** 暗箱：一个盒子，正面开小孔，背面有倒立的箭头像。 */
	const pinholeBox = (cx, cy, w, h, style = {}) => ({
		paths: [
			path([
				[cx - w / 2, cy - h / 2],
				[cx + w / 2, cy - h / 2],
				[cx + w / 2, cy + h / 2],
				[cx - w / 2, cy + h / 2],
				[cx - w / 2, cy - h / 2]
			], {
				width: .0018,
				alpha: .5,
				...style
			}),
			segment([cx - w / 2 - .25, cy - h * .35], [cx - w / 2 - .25, cy + h * .35], {
				width: .0016,
				alpha: .45,
				delay: (style.delay ?? 0) + .1
			}),
			segment([cx + w / 2 - .02, cy + h * .28], [cx + w / 2 - .02, cy - h * .28], {
				width: .0014,
				alpha: .4,
				delay: (style.delay ?? 0) + .3,
				dash: [.005, .005]
			}),
			segment([cx - w / 2 - .25, cy - h * .35], [cx + w / 2 - .02, cy + h * .28], {
				width: 8e-4,
				alpha: .3,
				delay: (style.delay ?? 0) + .2
			}),
			segment([cx - w / 2 - .25, cy + h * .35], [cx + w / 2 - .02, cy - h * .28], {
				width: 8e-4,
				alpha: .3,
				delay: (style.delay ?? 0) + .2
			})
		],
		nodes: [node([cx - w / 2, cy], .02, (style.delay ?? 0) + .3)]
	});
	const rings = (cx, cy, r, count, style = {}) => concentric(cx, cy, Array.from({ length: count }, (_, i) => r * Math.sqrt((i + 1) / count)), style);
	/** 光栅：一条带密集缝的横线。 */
	const grating = (cx, y, width, slits, style = {}) => ({
		paths: [segment([cx - width / 2, y], [cx + width / 2, y], {
			width: .0022,
			alpha: .55,
			...style
		}), ...Array.from({ length: slits }, (_, i) => {
			const x = cx - width * .3 + width * .6 * i / Math.max(1, slits - 1);
			return segment([x, y - .01], [x, y + .01], {
				width: 8e-4,
				alpha: .4,
				delay: (style.delay ?? 0) + .15 + i * .01,
				span: .1
			});
		})],
		nodes: []
	});
	/** 两个点源和各自的一圈圈波前。 */
	const twoSources = (cx, cy, separation, r, style = {}) => merge(concentric(cx - separation / 2, cy, [
		r * .25,
		r * .5,
		r * .75,
		r
	], {
		alpha: .3,
		...style
	}), concentric(cx + separation / 2, cy, [
		r * .25,
		r * .5,
		r * .75,
		r
	], {
		alpha: .3,
		...style,
		delay: (style.delay ?? 0) + .1
	}), {
		paths: [],
		nodes: [node([cx - separation / 2, cy], .024, .3), node([cx + separation / 2, cy], .024, .35, 1)]
	});
	/** 莫尔：两组细平行线，第二组略转一个角度。 */
	const moire = (cx, cy, size, count, rotation, style = {}) => {
		const set = (angle, delay) => Array.from({ length: count }, (_, i) => {
			const t = -.5 + i / (count - 1);
			const ox = Math.cos(angle + Math.PI / 2) * t * size;
			const oy = Math.sin(angle + Math.PI / 2) * t * size;
			return segment([cx + ox - Math.cos(angle) * size / 2, cy + oy - Math.sin(angle) * size / 2], [cx + ox + Math.cos(angle) * size / 2, cy + oy + Math.sin(angle) * size / 2], {
				width: 8e-4,
				alpha: .3,
				...style,
				delay: delay + i * .008,
				span: .15
			});
		});
		return {
			paths: [...set(0, style.delay ?? 0), ...set(rotation, (style.delay ?? 0) + .2)],
			nodes: []
		};
	};
	/** 驻波：几条上下对称的正弦（波腹与波节）。 */
	const standingWave = (x0, x1, y, amplitude, loops, style = {}) => ({
		paths: [
			-1,
			-.5,
			.5,
			1
		].map((k, j) => path(Array.from({ length: 121 }, (_, i) => {
			const t = i / 120;
			return [x0 + (x1 - x0) * t, y + Math.sin(t * loops * Math.PI) * amplitude * k];
		}), {
			width: .0012,
			alpha: .35 + Math.abs(k) * .2,
			...style,
			delay: (style.delay ?? 0) + j * .06,
			span: .5
		})),
		nodes: Array.from({ length: loops + 1 }, (_, i) => node([x0 + (x1 - x0) * i / loops, y], .014, (style.delay ?? 0) + .5, i))
	});
	/** 偏振片：两个圆，里面各一组平行线（第二组转过一个角度）。 */
	const polarizers = (cx, cy, r, gap, rotation, style = {}) => {
		const disc = (x, angle, delay) => [circle(x, cy, r, {
			width: .0016,
			alpha: .5,
			...style,
			delay
		}), ...Array.from({ length: 7 }, (_, i) => {
			const t = -.75 + i / 6 * 1.5;
			const half = Math.sqrt(Math.max(0, 1 - t * t)) * r;
			const ox = -Math.sin(angle) * t * r;
			const oy = Math.cos(angle) * t * r;
			return segment([x + ox - Math.cos(angle) * half, cy + oy - Math.sin(angle) * half], [x + ox + Math.cos(angle) * half, cy + oy + Math.sin(angle) * half], {
				width: 8e-4,
				alpha: .3,
				delay: delay + .15 + i * .02,
				span: .15
			});
		})];
		return {
			paths: [...disc(cx - gap / 2, 0, style.delay ?? 0), ...disc(cx + gap / 2, rotation, (style.delay ?? 0) + .2)],
			nodes: []
		};
	};
	/** 一片大叶：外轮廓、主脉与多对侧脉。 */
	const bigLeaf = (cx, cy, length, angle, style = {}) => {
		const d = [Math.cos(angle), Math.sin(angle)];
		const n = [-d[1], d[0]];
		const at = (along, across) => [cx + d[0] * length * (along - .5) + n[0] * across, cy + d[1] * length * (along - .5) + n[1] * across];
		const bulge = length * .3;
		const paths = [
			path(cubicPoints(at(0, 0), at(.3, bulge), at(.75, bulge * .8), at(1, 0), 48), {
				width: .002,
				alpha: .6,
				...style,
				span: .4
			}),
			path(cubicPoints(at(0, 0), at(.3, -bulge), at(.75, -bulge * .8), at(1, 0), 48), {
				width: .002,
				alpha: .6,
				...style,
				delay: (style.delay ?? 0) + .05,
				span: .4
			}),
			path([at(-.12, 0), at(1, 0)], {
				width: .0014,
				alpha: .5,
				delay: (style.delay ?? 0) + .15,
				span: .4
			})
		];
		for (let v = 1; v <= 6; v += 1) {
			const f = v / 7.5;
			for (const s of [1, -1]) paths.push(path(cubicPoints(at(f, 0), at(f + .05, s * bulge * .3), at(f + .1, s * bulge * .55), at(f + .15, s * bulge * (.8 - f * .35)), 16), {
				width: 9e-4,
				alpha: .4,
				delay: (style.delay ?? 0) + .25 + v * .05,
				span: .18
			}));
		}
		return {
			paths,
			nodes: [node(at(1, 0), .026, (style.delay ?? 0) + .6)]
		};
	};
	/** 林冠：从画面上方垂下的枝条（递归分叉）。 */
	const canopy = (aspect, random, style = {}) => {
		const paths = [];
		const grow = (x, y, angle, length, depth, delay) => {
			const x1 = x + Math.cos(angle) * length;
			const y1 = y + Math.sin(angle) * length;
			paths.push(segment([x, y], [x1, y1], {
				width: 8e-4 + depth * 5e-4,
				alpha: .3 + depth * .08,
				delay,
				span: .2
			}));
			if (depth <= 0) return;
			grow(x1, y1, angle - .35 - random() * .3, length * .72, depth - 1, delay + .08);
			grow(x1, y1, angle + .35 + random() * .3, length * .72, depth - 1, delay + .08);
		};
		[
			.08,
			.35,
			.65,
			.92
		].forEach((fx, i) => grow(fx * aspect, -.02, Math.PI / 2 + (random() - .5) * .6, .14, 4, (style.delay ?? 0) + i * .05));
		return {
			paths,
			nodes: []
		};
	};
	/** 蕨卷：对数螺线 + 两侧的小叶片。 */
	const fernCurl = (cx, cy, r, style = {}) => {
		const spiral = Array.from({ length: 140 }, (_, i) => {
			const t = i / 139;
			const a = t * TAU$2 * 2.2;
			const rr = r * Math.exp(-t * 2.4);
			return [cx + Math.cos(a) * rr, cy + Math.sin(a) * rr];
		});
		const stem = [[cx + r, cy], [cx + r, cy + r * 2.2]];
		const leaflets = Array.from({ length: 9 }, (_, i) => {
			const [x, y] = spiral[Math.floor(i / 9 * 90)];
			return circle(x, y, .008 + (1 - i / 9) * .01, {
				width: 8e-4,
				alpha: .35,
				delay: (style.delay ?? 0) + .4 + i * .03,
				span: .1
			});
		});
		return {
			paths: [
				path(stem, {
					width: .0016,
					alpha: .5,
					...style
				}),
				path(spiral, {
					width: .0016,
					alpha: .55,
					...style,
					delay: (style.delay ?? 0) + .1,
					span: .5
				}),
				...leaflets
			],
			nodes: []
		};
	};
	/** 种子：椭圆种子 + 向下长的须根。 */
	const seedRoots = (cx, cy, random, style = {}) => ({
		paths: [path(ellipsePath(cx, cy, .025, .035, .3), {
			width: .002,
			alpha: .6,
			...style
		}), ...Array.from({ length: 6 }, (_, i) => {
			const spread = (i - 2.5) * .05;
			return path(cubicPoints([cx, cy + .03], [cx + spread * .4, cy + .08], [cx + spread, cy + .12], [cx + spread * 1.4 + (random() - .5) * .04, cy + .2 + random() * .06], 24), {
				width: 9e-4,
				alpha: .4,
				delay: (style.delay ?? 0) + .2 + i * .05,
				span: .35
			});
		})],
		nodes: [node([cx, cy - .04], .03, (style.delay ?? 0) + .2)]
	});
	/** 花：以中心为原点的一圈花瓣（每片一条闭合曲线）。 */
	const bloom = (cx, cy, r, petals, style = {}) => ({
		paths: [...Array.from({ length: petals }, (_, i) => {
			const a = i / petals * TAU$2;
			const d = [Math.cos(a), Math.sin(a)];
			const n = [-d[1], d[0]];
			const tip = [cx + d[0] * r, cy + d[1] * r];
			const w = r * .35;
			return path([...cubicPoints([cx, cy], [cx + d[0] * r * .4 + n[0] * w, cy + d[1] * r * .4 + n[1] * w], [cx + d[0] * r * .9 + n[0] * w * .6, cy + d[1] * r * .9 + n[1] * w * .6], tip, 20), ...cubicPoints(tip, [cx + d[0] * r * .9 - n[0] * w * .6, cy + d[1] * r * .9 - n[1] * w * .6], [cx + d[0] * r * .4 - n[0] * w, cy + d[1] * r * .4 - n[1] * w], [cx, cy], 20)], {
				width: .0016,
				alpha: .5,
				...style,
				delay: (style.delay ?? 0) + i * .05,
				span: .3
			});
		}), circle(cx, cy, r * .12, {
			width: .0012,
			alpha: .5,
			delay: (style.delay ?? 0) + .4
		})],
		nodes: [node([cx, cy], .03, (style.delay ?? 0) + .5)]
	});
	/** 藤：一条向上攀的螺旋（正弦）+ 卷须。 */
	const vine = (x, bottom, top, amplitude, turns, style = {}) => ({
		paths: [path(Array.from({ length: 161 }, (_, i) => {
			const t = i / 160;
			return [x + Math.sin(t * turns * TAU$2) * amplitude * (1 - t * .4), bottom + (top - bottom) * t];
		}), {
			width: .0016,
			alpha: .55,
			...style,
			span: .7
		}), segment([x, bottom], [x, top], {
			width: 8e-4,
			alpha: .25,
			delay: (style.delay ?? 0) + .1,
			dash: [.005, .008],
			span: .5
		})],
		nodes: [node([x, top], .024, (style.delay ?? 0) + .7)]
	});
	/** 显微视野：一个大圆视野 + 里面一群细胞（小椭圆）。 */
	const cells = (cx, cy, r, random, count, style = {}) => ({
		paths: [circle(cx, cy, r, {
			width: .0022,
			alpha: .5,
			...style
		}), ...Array.from({ length: count }, (_, i) => {
			const a = random() * TAU$2;
			const d = Math.sqrt(random()) * r * .8;
			return path(ellipsePath(cx + Math.cos(a) * d, cy + Math.sin(a) * d, .012 + random() * .012, .008 + random() * .008, random() * Math.PI), {
				width: 9e-4,
				alpha: .4,
				delay: (style.delay ?? 0) + .15 + i * .02,
				span: .15
			});
		})],
		nodes: []
	});
	/** 分子：一个苯环（六边形 + 内圆）+ 几根键。 */
	const molecule = (cx, cy, r, style = {}) => {
		const hex = Array.from({ length: 7 }, (_, i) => [cx + Math.cos(i * TAU$2 / 6 + Math.PI / 6) * r, cy + Math.sin(i * TAU$2 / 6 + Math.PI / 6) * r]);
		return {
			paths: [
				path(hex, {
					width: .0018,
					alpha: .55,
					...style
				}),
				circle(cx, cy, r * .55, {
					width: .001,
					alpha: .35,
					delay: (style.delay ?? 0) + .2
				}),
				...[
					0,
					2,
					4
				].map((k, i) => {
					const [x, y] = hex[k];
					return segment([x, y], [x + (x - cx) * .8, y + (y - cy) * .8], {
						width: .0012,
						alpha: .45,
						delay: (style.delay ?? 0) + .3 + i * .05
					});
				})
			],
			nodes: [
				0,
				2,
				4
			].map((k, i) => {
				const [x, y] = hex[k];
				return node([x + (x - cx) * .8, y + (y - cy) * .8], .02, (style.delay ?? 0) + .45, i);
			})
		};
	};
	/** 轨道：一组同心椭圆（倾斜），每条上有一颗行星（闪点）。 */
	const orbits = (cx, cy, radii, tilt, flatten, random, style = {}) => ({
		paths: radii.map((r, i) => path(ellipsePath(cx, cy, r, r * flatten, tilt), {
			width: .0012,
			alpha: .45,
			...style,
			delay: (style.delay ?? 0) + i * .07,
			span: .5
		})),
		nodes: [node([cx, cy], .04, (style.delay ?? 0) + .2), ...radii.map((r, i) => {
			const a = random() * TAU$2;
			const [x, y] = ellipsePath(cx, cy, r, r * flatten, tilt, a, a, 1)[0];
			return node([x, y], .018 + random() * .012, (style.delay ?? 0) + .5 + i * .05, i);
		})]
	});
	/** 星轨：围绕天极的一圈圈弧。 */
	const starTrails = (cx, cy, random, count, maxR, style = {}) => ({
		paths: Array.from({ length: count }, (_, i) => {
			const r = maxR * (.1 + random() * .9);
			const a = random() * TAU$2;
			const span = .4 + random() * .8;
			return path(arcPoints(cx, cy, r, a, a + span, 24), {
				width: 8e-4 + random() * 8e-4,
				alpha: .25 + random() * .3,
				...style,
				delay: (style.delay ?? 0) + i / count * .4,
				span: .3
			});
		}),
		nodes: [node([cx, cy], .03, (style.delay ?? 0) + .1)]
	});
	/** 六分仪：60° 的刻度弧、两条半径与一条视线。 */
	const sextant = (cx, cy, r, style = {}) => merge({
		paths: [
			path(arcPoints(cx, cy, r, Math.PI * .33, Math.PI * .67, 48), {
				width: .002,
				alpha: .6,
				...style
			}),
			segment([cx, cy], [cx + Math.cos(Math.PI * .33) * r, cy + Math.sin(Math.PI * .33) * r], {
				width: .0014,
				alpha: .5,
				delay: (style.delay ?? 0) + .1
			}),
			segment([cx, cy], [cx + Math.cos(Math.PI * .67) * r, cy + Math.sin(Math.PI * .67) * r], {
				width: .0014,
				alpha: .5,
				delay: (style.delay ?? 0) + .12
			}),
			segment([cx, cy], [cx + Math.cos(Math.PI * .45) * r * 1.1, cy + Math.sin(Math.PI * .45) * r * 1.1], {
				width: .001,
				alpha: .45,
				delay: (style.delay ?? 0) + .3,
				dash: [.006, .006]
			}),
			segment([cx - r * .9, cy - r * .2], [cx + r * .2, cy - r * .05], {
				width: 9e-4,
				alpha: .35,
				delay: (style.delay ?? 0) + .35,
				dash: [.004, .006]
			})
		],
		nodes: [node([cx, cy], .022, (style.delay ?? 0) + .3)]
	}, rays(cx, cy, r * .94, r, 30, Math.PI * .33, Math.PI * .67, {
		delay: (style.delay ?? 0) + .2,
		alpha: .4
	}));
	/** 星座：几颗星（闪点）连成折线。 */
	const constellation = (aspect, random, count, region, style = {}) => {
		const [x0, y0, x1, y1] = region;
		const stars = Array.from({ length: count }, () => [(x0 + random() * (x1 - x0)) * aspect, y0 + random() * (y1 - y0)]);
		stars.sort((a, b) => a[0] - b[0]);
		return {
			paths: stars.slice(1).map((star, i) => segment(stars[i], star, {
				width: .001,
				alpha: .4,
				...style,
				delay: (style.delay ?? 0) + .2 + i * .06,
				span: .2
			})),
			nodes: stars.map((at, i) => node(at, .018 + random() * .016, (style.delay ?? 0) + i * .05, i))
		};
	};
	/** 日冕 / 日食：一个圆盘（黑盘的边）+ 一圈长短不一的放射丝。 */
	const corona = (cx, cy, r, random, style = {}) => ({
		paths: [circle(cx, cy, r, {
			width: .0024,
			alpha: .65,
			...style
		}), ...Array.from({ length: 40 }, (_, i) => {
			const a = i / 40 * TAU$2 + random() * .05;
			const len = r * (.25 + random() * .9);
			return segment([cx + Math.cos(a) * r * 1.05, cy + Math.sin(a) * r * 1.05], [cx + Math.cos(a) * (r + len), cy + Math.sin(a) * (r + len)], {
				width: 8e-4,
				alpha: .25 + random() * .3,
				delay: (style.delay ?? 0) + .2 + i / 40 * .3,
				span: .15
			});
		})],
		nodes: [node([cx + r * .7, cy - r * .7], .03, (style.delay ?? 0) + .5)]
	});
	/** 新月：两段弧围成的月牙。 */
	const crescent = (cx, cy, r, style = {}) => ({
		paths: [path(arcPoints(cx, cy, r, Math.PI * .35, Math.PI * 1.65, 60), {
			width: .002,
			alpha: .6,
			...style
		}), path(arcPoints(cx + r * .45, cy, r * .85, Math.PI * .58, Math.PI * 1.42, 60), {
			width: .0012,
			alpha: .4,
			delay: (style.delay ?? 0) + .15
		})],
		nodes: []
	});
	/** 浑天仪：几个倾斜的环 + 一根轴。 */
	const armillary = (cx, cy, r, style = {}) => ({
		paths: [
			circle(cx, cy, r, {
				width: .002,
				alpha: .55,
				...style
			}),
			path(ellipsePath(cx, cy, r, r * .3, 0), {
				width: .0014,
				alpha: .45,
				delay: (style.delay ?? 0) + .1
			}),
			path(ellipsePath(cx, cy, r, r * .3, .41), {
				width: .0014,
				alpha: .45,
				delay: (style.delay ?? 0) + .15
			}),
			path(ellipsePath(cx, cy, r * .3, r, 0), {
				width: .0012,
				alpha: .4,
				delay: (style.delay ?? 0) + .2
			}),
			segment([cx - Math.sin(.41) * r * 1.25, cy - Math.cos(.41) * r * 1.25], [cx + Math.sin(.41) * r * 1.25, cy + Math.cos(.41) * r * 1.25], {
				width: .0012,
				alpha: .45,
				delay: (style.delay ?? 0) + .3
			})
		],
		nodes: [node([cx, cy], .03, (style.delay ?? 0) + .3)]
	});
	/** 流星：几条斜向的光迹（头上有闪点）。 */
	const meteors = (aspect, random, count, style = {}) => {
		const paths = [];
		const nodes = [];
		for (let i = 0; i < count; i += 1) {
			const x = (.2 + random() * .8) * aspect;
			const y = random() * .5;
			const len = .12 + random() * .25;
			const head = [x - len * .8, y + len * .6];
			paths.push(segment([x, y], head, {
				width: .0012 + random() * .001,
				alpha: .45,
				...style,
				delay: (style.delay ?? 0) + i * .08,
				span: .2
			}));
			nodes.push(node(head, .02, (style.delay ?? 0) + i * .08 + .2, i));
		}
		return {
			paths,
			nodes
		};
	};
	/** 灯头：光源处的小梯形灯罩（朝向光束方向）。 */
	const lampHead = (x, y, angle, size, style = {}) => {
		const d = [Math.cos(angle), Math.sin(angle)];
		const n = [-d[1], d[0]];
		const back = [x - d[0] * size, y - d[1] * size];
		const corners = [
			[back[0] + n[0] * size * .35, back[1] + n[1] * size * .35],
			[x + n[0] * size * .6, y + n[1] * size * .6],
			[x - n[0] * size * .6, y - n[1] * size * .6],
			[back[0] - n[0] * size * .35, back[1] - n[1] * size * .35]
		];
		return {
			paths: [path([...corners, corners[0]], {
				width: .0018,
				alpha: .55,
				span: .3,
				...style
			})],
			nodes: []
		};
	};
	/** 帷幕：两侧各一片带褶皱的幕布（竖向波浪线）。 */
	const curtains = (aspect, gap, style = {}) => ({
		paths: [-1, 1].flatMap((side) => Array.from({ length: 6 }, (_, i) => {
			const base = aspect / 2 + side * (gap / 2 + i * .05);
			return path(Array.from({ length: 41 }, (_, k) => {
				const t = k / 40;
				return [base + Math.sin(t * 5 + i) * .008 * side, -.02 + t * 1.04];
			}), {
				width: .001 + (5 - i) * 2e-4,
				alpha: .2 + (5 - i) * .05,
				...style,
				delay: (style.delay ?? 0) + i * .05,
				span: .5
			});
		})),
		nodes: []
	});
	/** 放映机：侧面的机身、两个片盘。 */
	const projector = (x, y, size, style = {}) => ({
		paths: [
			path([
				[x - size, y - size * .3],
				[x, y - size * .3],
				[x, y + size * .3],
				[x - size, y + size * .3],
				[x - size, y - size * .3]
			], {
				width: .0018,
				alpha: .55,
				...style
			}),
			circle(x - size * .75, y - size * .65, size * .33, {
				width: .0014,
				alpha: .5,
				delay: (style.delay ?? 0) + .1
			}),
			circle(x - size * .25, y - size * .65, size * .33, {
				width: .0014,
				alpha: .5,
				delay: (style.delay ?? 0) + .15
			}),
			path([
				[x, y - size * .12],
				[x + size * .18, y - size * .18],
				[x + size * .18, y + size * .18],
				[x, y + size * .12]
			], {
				width: .0014,
				alpha: .5,
				delay: (style.delay ?? 0) + .2
			})
		],
		nodes: [node([x + size * .18, y], .024, (style.delay ?? 0) + .3)]
	});
	/** 一条地平线（带短刻度）。 */
	const horizon = (aspect, y, style = {}) => ({
		paths: [segment([0, y], [aspect, y], {
			width: .0014,
			alpha: .4,
			...style,
			span: .6
		}), ...Array.from({ length: 16 }, (_, i) => segment([aspect * (i + .5) / 16, y], [aspect * (i + .5) / 16, y + .012], {
			width: 8e-4,
			alpha: .3,
			delay: (style.delay ?? 0) + .3 + i * .01,
			span: .08
		}))],
		nodes: []
	});

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/rigs/base.ts
	const TOP = {
		x: .5,
		y: -.04
	};
	const DOWN = Math.PI / 2;
	/** 主光柱：窄、亮、束内有一丝丝的亮纹。 */
	const shaft = (overrides = {}) => ({
		...TOP,
		angle: DOWN,
		spread: .09,
		width: .05,
		length: .8,
		softness: .6,
		intensity: .95,
		streaks: .6,
		streakFreq: 7,
		streakSpeed: .08,
		core: .75,
		sway: {
			amplitude: .012,
			period: 13,
			phase: 0
		},
		...overrides
	});
	/** 主光柱外的一圈放射光扇。 */
	const fan = (overrides = {}) => ({
		...TOP,
		angle: DOWN,
		spread: .3,
		length: .6,
		softness: .9,
		intensity: .32,
		streaks: .85,
		streakFreq: 26,
		streakSpeed: .05,
		core: .4,
		sway: {
			amplitude: .02,
			period: 17,
			phase: .3
		},
		...overrides
	});
	const FOG = {
		density: 1,
		tyndallBase: .12,
		scale: 2.4,
		driftX: .008,
		driftY: -.04,
		warp: .9,
		ambient: .03
	};
	const GLARE = {
		x: .5,
		y: 0,
		radius: .12,
		intensity: .9,
		streak: .45
	};
	const MOTES = {
		count: 260,
		sizeMin: .004,
		sizeMax: .013,
		driftX: .004,
		driftY: -.01,
		swirl: .02,
		swirlPeriod: 9,
		twinkle: .5,
		ambient: .015,
		gain: 1.3
	};
	const FRONT = {
		count: 9,
		sizeMin: .05,
		sizeMax: .15,
		driftX: .006,
		driftY: -.002,
		swirl: .012,
		swirlPeriod: 14,
		twinkle: .2,
		ambient: .02,
		gain: .3
	};
	const STARFALL = {
		stars: 420,
		opening: 3.2,
		rain: 70,
		rainSpeed: .22,
		rainSpread: .35,
		brightness: 1
	};
	/** 量角器光环 + 取景框 + 散落闪点（参考图的线稿）。 */
	const standardArt = ({ aspect, random }, haloRadius = .3) => mergeSpecs(protractorHalo({
		cx: aspect * .5,
		cy: .03,
		radius: haloRadius,
		alpha: .55
	}), viewfinderFrame({
		aspect,
		left: .07,
		top: .21,
		right: .93,
		bottom: .8,
		delay: .1,
		alpha: .32
	}), scatteredSparks({
		aspect,
		count: 16,
		random,
		delay: .3
	}));
	const ellipse = (cx, cy, rx, ry, steps = 96) => arcPoints(0, 0, 1, 0, Math.PI * 2, steps).map(([x, y]) => [cx + x * rx, cy + y * ry]);
	const rig = (beams, fog = {}, glare = GLARE, modules = {}) => () => ({
		beams,
		fog: {
			...FOG,
			...fog
		},
		glare,
		...modules
	});
	/** 按族填默认值的光位构造器：各光位只写不同的地方。 */
	const familyOf = (family, defaults = {}) => (profile) => ({
		family,
		motes: MOTES,
		front: FRONT,
		lineArt: (context) => standardArt(context),
		region: {
			cx: .5,
			cy: .54,
			w: .72,
			h: .5
		},
		heroSize: .085,
		camera: {
			push: .035,
			driftX: 0,
			driftY: -.006
		},
		...defaults,
		...profile
	});

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/rigs/astral.ts
	const astral = familyOf("astral", {
		starfall: {
			...STARFALL,
			stars: 620,
			rain: 20,
			brightness: 1.1
		},
		motes: {
			...MOTES,
			count: 160
		},
		artGain: 2
	});
	const sparks$6 = ({ aspect, random }) => scatteredSparks({
		aspect,
		count: 20,
		random,
		delay: .3
	});
	/** 从圆盘 (cx, cy, r) 的边缘向外放射的一圈光束（日冕、日食）。 */
	const radial = (cx, cy, r, aspect, count, overrides = {}) => Array.from({ length: count }, (_, k) => {
		const angle = k / count * Math.PI * 2 + .3;
		return shaft({
			x: cx + Math.cos(angle) * r / aspect,
			y: cy + Math.sin(angle) * r,
			angle,
			spread: .14,
			width: .04,
			length: .35,
			softness: .8,
			intensity: .7,
			streaks: .7,
			streakFreq: 12,
			core: .3,
			sway: {
				amplitude: .04,
				period: 9 + k,
				phase: k / count
			},
			...overrides
		});
	});
	/** 日食（l）：黑盘周围一圈日冕光，边上一颗贝利珠闪光。 */
	const astralEclipse = astral({
		kind: "astral-eclipse",
		label: "日食",
		mood: "loud",
		light: ({ aspect }) => ({
			beams: radial(.5, .34, .13, aspect, 6),
			fog: {
				density: .8,
				tyndallBase: .2,
				scale: 2.4,
				driftX: .004,
				driftY: -.01,
				warp: .9,
				ambient: .02
			},
			glare: {
				x: .5 + .13 * Math.cos(-.8) / aspect,
				y: .34 + .13 * Math.sin(-.8),
				radius: .05,
				intensity: 1.3,
				streak: .9
			}
		}),
		lineArt: ({ aspect, random }) => merge(corona(aspect * .5, .34, .13, random), sparks$6({
			aspect,
			random
		})),
		region: {
			cx: .5,
			cy: .7,
			w: .72,
			h: .4
		},
		typography: "crossed",
		burst: {
			mode: "sung",
			density: .35,
			every: 1,
			offset: 0,
			size: 3.6,
			tint: [
				1,
				.6,
				.45
			]
		},
		decay: {
			strength: 1.3,
			delay: .7
		}
	});
	/** 轨道（q）：同心的椭圆轨道，行星光点在上面。 */
	const astralOrbit = astral({
		kind: "astral-orbit",
		label: "轨道",
		mood: "quiet",
		light: rig([shaft({
			spread: .05,
			intensity: .45,
			reach: .4
		})], {
			density: .6,
			ambient: .015
		}, {
			x: .5,
			y: .42,
			radius: .05,
			intensity: 1,
			streak: .5
		}),
		lineArt: ({ aspect, random }) => merge(orbits(aspect * .5, .42, [
			.12,
			.2,
			.3,
			.42
		], -.18, .34, random), sparks$6({
			aspect,
			random
		})),
		region: {
			cx: .5,
			cy: .74,
			w: .72,
			h: .32
		},
		typography: "horizontal",
		decay: {
			strength: .6,
			delay: 1.6
		}
	});
	/** 星轨（q）：绕着天极缓慢旋转的一圈圈星轨。 */
	const astralTrail = astral({
		kind: "astral-trail",
		label: "星轨",
		mood: "quiet",
		light: rig([shaft({
			x: .72,
			spread: .04,
			intensity: .35
		})], {
			density: .5,
			ambient: .01
		}, null),
		lineArt: ({ aspect, random }) => merge(starTrails(aspect * .72, .12, random, 60, .9)),
		region: {
			cx: .36,
			cy: .54,
			w: .44,
			h: .6
		},
		typography: "vertical",
		decay: {
			strength: .5,
			delay: 1.8
		}
	});
	/** 六分仪（n）：刻度弧、两条半径与一条对准太阳的视线。 */
	const astralSextant = astral({
		kind: "astral-sextant",
		label: "六分仪",
		mood: "neutral",
		light: rig([shaft({
			x: .85,
			y: -.05,
			angle: 2.2,
			spread: .05,
			width: .03,
			length: 1.4,
			intensity: .9
		})], { density: .8 }, {
			...GLARE,
			x: .85,
			y: -.02
		}),
		lineArt: ({ aspect, random }) => merge(sextant(aspect * .4, .14, .32), sparks$6({
			aspect,
			random
		})),
		region: {
			cx: .52,
			cy: .66,
			w: .72,
			h: .4
		},
		typography: "horizontal",
		decay: {
			strength: 1,
			delay: 1
		}
	});
	/** 星座（n）：星星连成一个星座，字像是星名。 */
	const astralConstellation = astral({
		kind: "astral-constellation",
		label: "星座",
		mood: "neutral",
		light: rig([shaft({
			spread: .06,
			intensity: .4
		})], {
			density: .6,
			ambient: .015
		}, {
			...GLARE,
			intensity: .4
		}),
		lineArt: ({ aspect, random }) => merge(constellation(aspect, random, 8, [
			.1,
			.1,
			.9,
			.42
		]), constellation(aspect, random, 5, [
			.6,
			.62,
			.95,
			.9
		], { delay: .3 })),
		region: {
			cx: .42,
			cy: .66,
			w: .6,
			h: .4
		},
		typography: "horizontal",
		decay: {
			strength: 1,
			delay: 1
		}
	});
	/** 日冕（l）：太阳在画面上方，边缘放射出丝丝的冕光。 */
	const astralCorona = astral({
		kind: "astral-corona",
		label: "日冕",
		mood: "loud",
		light: ({ aspect }) => ({
			beams: radial(.5, .12, .1, aspect, 6, {
				length: .6,
				intensity: .9
			}),
			fog: {
				density: 1,
				tyndallBase: .15,
				scale: 2.4,
				driftX: .004,
				driftY: -.02,
				warp: .9,
				ambient: .03
			},
			glare: {
				x: .5,
				y: .12,
				radius: .16,
				intensity: 1.3,
				streak: .7
			}
		}),
		lineArt: ({ aspect, random }) => merge(corona(aspect * .5, .12, .1, random), sparks$6({
			aspect,
			random
		})),
		region: {
			cx: .5,
			cy: .62,
			w: .72,
			h: .46
		},
		typography: "crossed",
		burst: {
			mode: "sung",
			density: .3,
			every: 2,
			offset: 0,
			size: 3.4,
			tint: [
				1,
				.65,
				.4
			]
		},
		decay: {
			strength: 1.3,
			delay: .7
		}
	});
	/** 月光（q）：右上角一弯新月，冷色的光斜照下来，云雾流动。 */
	const astralMoon = astral({
		kind: "astral-moon",
		label: "月光",
		mood: "quiet",
		light: rig([shaft({
			x: .82,
			y: .12,
			angle: 2.1,
			spread: .12,
			width: .08,
			length: 1.4,
			softness: .8,
			intensity: .75,
			tint: [
				.72,
				.82,
				1
			]
		})], {
			density: 1.2,
			driftX: -.01,
			driftY: 0,
			ambient: .035,
			warp: 1.3
		}, {
			x: .82,
			y: .12,
			radius: .1,
			intensity: .8,
			streak: .2
		}),
		lineArt: ({ aspect, random }) => merge(crescent(aspect * .82, .12, .07), sparks$6({
			aspect,
			random
		})),
		region: {
			cx: .38,
			cy: .54,
			w: .5,
			h: .62
		},
		typography: "vertical",
		decay: {
			strength: .6,
			delay: 1.6
		}
	});
	/** 星云（n）：大片带颜色的烟，光从里面透出来。 */
	const astralNebula = astral({
		kind: "astral-nebula",
		label: "星云",
		mood: "neutral",
		light: rig([shaft({
			x: .4,
			y: .45,
			angle: -.6,
			spread: .6,
			width: .1,
			length: .8,
			softness: 1,
			intensity: .5,
			streaks: .6,
			streakFreq: 3,
			core: .2,
			spectrum: .7,
			sway: {
				amplitude: .2,
				period: 20,
				phase: 0
			}
		}), shaft({
			x: .6,
			y: .45,
			angle: 2.4,
			spread: .6,
			width: .1,
			length: .8,
			softness: 1,
			intensity: .4,
			streaks: .6,
			streakFreq: 3,
			core: .2,
			spectrum: .7,
			sway: {
				amplitude: .2,
				period: 23,
				phase: .5
			}
		})], {
			density: 1.4,
			ambient: .06,
			warp: 1.6,
			scale: 1.6,
			driftX: .006,
			driftY: .002
		}, {
			x: .5,
			y: .45,
			radius: .08,
			intensity: .7,
			streak: .2
		}),
		lineArt: sparks$6,
		typography: "horizontal",
		decay: {
			strength: 1,
			delay: 1
		}
	});
	/** 浑天（n）：浑天仪的几个环缓慢转动。 */
	const astralArmillary = astral({
		kind: "astral-armillary",
		label: "浑天",
		mood: "neutral",
		light: rig([shaft(), fan({ intensity: .2 })]),
		lineArt: ({ aspect, random }) => merge(armillary(aspect * .5, .42, .26), sparks$6({
			aspect,
			random
		})),
		region: {
			cx: .5,
			cy: .5,
			w: .5,
			h: .62
		},
		typography: "vertical",
		decay: {
			strength: 1,
			delay: 1
		}
	});
	/** 流星（l）：几道斜着划过的光迹与拖尾。 */
	const astralMeteor = astral({
		kind: "astral-meteor",
		label: "流星",
		mood: "loud",
		light: rig([
			0,
			1,
			2
		].map((i) => shaft({
			x: .95 - i * .2,
			y: -.02 + i * .05,
			angle: 2.5,
			spread: .01,
			width: .006,
			length: .6,
			intensity: .9,
			streaks: .2,
			core: .8,
			reach: .5 + i * .1,
			pulse: {
				amplitude: .7,
				period: 2.2 + i * .7,
				phase: i * .3
			},
			sway: void 0
		})), {
			density: .7,
			ambient: .015
		}, null),
		lineArt: ({ aspect, random }) => merge(meteors(aspect, random, 6), sparks$6({
			aspect,
			random
		})),
		typography: "crossed",
		decay: {
			strength: 1.3,
			delay: .7
		},
		starfall: {
			...STARFALL,
			stars: 620,
			rain: 60,
			rainSpeed: .6,
			rainSpread: 1.8,
			brightness: 1.2
		}
	});
	const ASTRAL_PROFILES = [
		astralEclipse,
		astralOrbit,
		astralTrail,
		astralSextant,
		astralConstellation,
		astralCorona,
		astralMoon,
		astralNebula,
		astralArmillary,
		astralMeteor
	];

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/rigs/botany.ts
	const botanySprout = {
		kind: "botany-sprout",
		label: "萌芽",
		family: "botany",
		mood: "quiet",
		light: () => ({
			beams: [{
				x: .5,
				y: -.05,
				angle: Math.PI / 2,
				spread: .075,
				width: .07,
				length: 1.2,
				softness: .7,
				intensity: .95,
				streaks: .5,
				streakFreq: 6,
				streakSpeed: .06,
				core: .7,
				sway: {
					amplitude: .008,
					period: 15,
					phase: .2
				}
			}, {
				x: .5,
				y: -.05,
				angle: Math.PI / 2,
				spread: .26,
				length: .75,
				softness: .95,
				intensity: .42,
				streaks: .85,
				streakFreq: 30,
				streakSpeed: .04,
				core: .3
			}],
			fog: {
				density: .9,
				tyndallBase: .14,
				scale: 2,
				driftX: -.006,
				driftY: -.03,
				warp: 1.1,
				ambient: .025
			},
			glare: {
				x: .5,
				y: -.01,
				radius: .1,
				intensity: .9,
				streak: .35
			}
		}),
		motes: {
			count: 200,
			sizeMin: .004,
			sizeMax: .011,
			driftX: -.003,
			driftY: -.012,
			swirl: .018,
			swirlPeriod: 11,
			twinkle: .55,
			ambient: .012,
			gain: 1.2
		},
		front: {
			count: 6,
			sizeMin: .05,
			sizeMax: .12,
			driftX: -.004,
			driftY: -.002,
			swirl: .01,
			swirlPeriod: 16,
			twinkle: .2,
			ambient: .015,
			gain: .25
		},
		lineArt: ({ aspect, random }) => mergeSpecs(sprout({
			cx: aspect * .5,
			baseY: .78,
			size: .22,
			alpha: 1
		}), protractorHalo({
			cx: aspect * .5,
			cy: .02,
			radius: .22,
			alpha: .35,
			delay: .2
		}), viewfinderFrame({
			aspect,
			left: .07,
			top: .22,
			right: .93,
			bottom: .8,
			delay: .25,
			alpha: .22
		}), scatteredSparks({
			aspect,
			count: 12,
			random,
			delay: .35,
			region: [
				.2,
				.3,
				.8,
				.9
			]
		})),
		region: {
			cx: .5,
			cy: .44,
			w: .72,
			h: .6
		},
		heroSize: .08,
		echo: { size: .28 },
		typography: "vertical",
		decay: {
			strength: .75,
			delay: 1.2
		},
		starfall: {
			stars: 300,
			opening: 3.6,
			rain: 50,
			rainSpeed: .16,
			rainSpread: .25,
			brightness: .85
		},
		camera: {
			push: .03,
			driftX: 0,
			driftY: .004
		}
	};
	const botany = familyOf("botany", {
		motes: {
			...MOTES,
			count: 220,
			driftY: -.012
		},
		artGain: 1.7,
		starfall: {
			...STARFALL,
			rain: 40
		}
	});
	const sparks$5 = ({ aspect, random }) => scatteredSparks({
		aspect,
		count: 12,
		random,
		delay: .3
	});
	/** 叶脉（n）：一片大叶斜在画面上，脉络被斜光一段段点亮。 */
	const botanyVeins = botany({
		kind: "botany-veins",
		label: "叶脉",
		mood: "neutral",
		light: rig([shaft({
			x: .15,
			y: -.05,
			angle: 1.1,
			spread: .12,
			width: .12,
			length: 1.4,
			intensity: .9,
			streaks: .5,
			streakFreq: 6
		})], { density: .9 }, {
			...GLARE,
			x: .15,
			y: -.02,
			radius: .14,
			intensity: .8
		}),
		lineArt: (context) => merge(bigLeaf(context.aspect * .52, .42, .62, -.45), sparks$5(context)),
		region: {
			cx: .5,
			cy: .72,
			w: .72,
			h: .36
		},
		typography: "horizontal",
		decay: {
			strength: 1,
			delay: 1
		}
	});
	/** 林冠（l）：仰视树冠，几道光从叶间射下。 */
	const botanyCanopy = botany({
		kind: "botany-canopy",
		label: "林冠",
		mood: "loud",
		light: rig([
			.25,
			.48,
			.7
		].map((x, i) => shaft({
			x,
			y: -.05,
			angle: 1.45 + i * .1,
			spread: .08,
			width: .08,
			length: 1.3,
			softness: .6,
			intensity: .85,
			streaks: .5,
			streakFreq: 6,
			gobo: {
				pattern: "leaves",
				frequency: 9,
				duty: .5,
				drift: .14
			},
			sway: {
				amplitude: .03,
				period: 6 + i,
				phase: i * .3
			}
		})), {
			density: 1.1,
			driftX: .008
		}, null),
		lineArt: ({ aspect, random }) => merge(canopy(aspect, random), scatteredSparks({
			aspect,
			count: 16,
			random,
			delay: .3
		})),
		motes: {
			...MOTES,
			count: 300,
			driftX: .008,
			driftY: .006,
			swirl: .03
		},
		typography: "crossed",
		decay: {
			strength: 1.2,
			delay: .8
		},
		starfall: {
			...STARFALL,
			rain: 90,
			rainSpread: 1.2
		}
	});
	/** 蕨卷（n）：一枝蕨叶卷曲着，随唱缓缓展开。 */
	const botanyFern = botany({
		kind: "botany-fern",
		label: "蕨卷",
		mood: "neutral",
		light: rig([shaft({
			x: .3,
			spread: .08,
			intensity: .85
		}), fan({
			x: .3,
			intensity: .2
		})]),
		lineArt: (context) => merge(fernCurl(context.aspect * .28, .42, .16), sparks$5(context)),
		region: {
			cx: .62,
			cy: .5,
			w: .5,
			h: .62
		},
		typography: "vertical",
		decay: {
			strength: .8,
			delay: 1.2
		}
	});
	/** 种子（q）：一道光落在一粒种子上，根须往下长。 */
	const botanySeed = botany({
		kind: "botany-seed",
		label: "种子",
		mood: "quiet",
		light: rig([shaft({
			spread: .04,
			width: .02,
			length: 1.2,
			intensity: .8,
			core: .9
		})], {
			density: .7,
			ambient: .02
		}, {
			...GLARE,
			intensity: .6
		}),
		lineArt: ({ aspect, random }) => merge(seedRoots(aspect * .5, .7, random), scatteredSparks({
			aspect,
			count: 10,
			random,
			delay: .3
		})),
		region: {
			cx: .5,
			cy: .38,
			w: .72,
			h: .4
		},
		typography: "horizontal",
		decay: {
			strength: .6,
			delay: 1.6
		}
	});
	/** 绽放（l）：以光点为中心，花瓣线稿一片片打开。 */
	const botanyBloom = botany({
		kind: "botany-bloom",
		label: "绽放",
		mood: "loud",
		light: rig(Array.from({ length: 6 }, (_, k) => shaft({
			x: .5,
			y: .42,
			angle: k / 6 * Math.PI * 2 + .26,
			spread: .12,
			width: .01,
			length: .6,
			softness: .7,
			intensity: .55,
			streaks: .5,
			streakFreq: 7,
			core: .4,
			reach: .45,
			sway: {
				amplitude: .05,
				period: 10,
				phase: k / 6
			}
		})), { density: .9 }, {
			x: .5,
			y: .42,
			radius: .1,
			intensity: 1.2,
			streak: .4
		}),
		lineArt: (context) => merge(bloom(context.aspect * .5, .42, .2, 8), sparks$5(context)),
		typography: "crossed",
		burst: {
			mode: "sweep",
			density: .5,
			every: 3,
			offset: 1,
			size: 3,
			tint: [
				1,
				.8,
				.7
			]
		},
		decay: {
			strength: 1.2,
			delay: .8
		}
	});
	/** 藤旋（n）：藤蔓螺旋着向光攀升。 */
	const botanyVine = botany({
		kind: "botany-vine",
		label: "藤旋",
		mood: "neutral",
		light: rig([shaft({
			x: .72,
			spread: .07,
			intensity: .9
		}), fan({
			x: .72,
			intensity: .2
		})], {}, {
			...GLARE,
			x: .72
		}),
		lineArt: (context) => merge(vine(context.aspect * .72, 1.02, .08, .05, 4), sparks$5(context)),
		region: {
			cx: .36,
			cy: .5,
			w: .5,
			h: .62
		},
		typography: "vertical",
		decay: {
			strength: 1,
			delay: 1
		}
	});
	/** 叶绿（q）：显微镜视野里，叶绿体的小圆点在光里发亮。 */
	const botanyChloro = botany({
		kind: "botany-chloro",
		label: "叶绿",
		mood: "quiet",
		light: rig([shaft({
			spread: .2,
			width: .3,
			softness: .9,
			intensity: .6,
			core: .3
		})], { density: .7 }, null, { caustic: {
			scale: 6,
			speed: .12,
			inBeam: .3,
			floor: {
				cx: .5,
				cy: .45,
				rx: .2,
				ry: .36,
				strength: .35
			}
		} }),
		lineArt: ({ aspect, random }) => merge(cells(aspect * .5, .45, .36, random, 22), scatteredSparks({
			aspect,
			count: 8,
			random,
			delay: .3
		})),
		region: {
			cx: .5,
			cy: .5,
			w: .5,
			h: .5
		},
		typography: "crossed",
		decay: {
			strength: .6,
			delay: 1.6
		}
	});
	/** 光合式（n）：分子结构的线稿与量角器光环，致敬参考图里的化学式。 */
	const botanyFormula = botany({
		kind: "botany-formula",
		label: "光合式",
		mood: "neutral",
		light: rig([shaft(), fan()]),
		lineArt: (context) => merge(molecule(context.aspect * .24, .3, .06), molecule(context.aspect * .78, .74, .05, { delay: .2 }), protractorHalo({
			cx: context.aspect * .5,
			cy: .03,
			radius: .28,
			alpha: .5
		}), sparks$5(context)),
		typography: "horizontal",
		decay: {
			strength: 1,
			delay: 1
		}
	});
	/** 花粉（l）：斜光里漂着大颗的花粉粒子。 */
	const botanyPollen = botany({
		kind: "botany-pollen",
		label: "花粉",
		mood: "loud",
		light: rig([shaft({
			x: .2,
			y: -.05,
			angle: 1.15,
			spread: .14,
			width: .16,
			length: 1.5,
			intensity: 1,
			streaks: .5
		}), fan({
			x: .2,
			angle: 1.15,
			intensity: .3
		})], {
			density: 1,
			driftX: .012,
			driftY: -.01
		}, {
			...GLARE,
			x: .2,
			y: 0
		}),
		lineArt: sparks$5,
		motes: {
			count: 360,
			sizeMin: .006,
			sizeMax: .022,
			driftX: .012,
			driftY: -.006,
			swirl: .04,
			swirlPeriod: 10,
			twinkle: .4,
			ambient: .02,
			gain: 1.6
		},
		typography: "horizontal",
		decay: {
			strength: 1.2,
			delay: .8
		}
	});
	const BOTANY_PROFILES = [
		botanySprout,
		botanyVeins,
		botanyCanopy,
		botanyFern,
		botanySeed,
		botanyBloom,
		botanyVine,
		botanyChloro,
		botanyFormula,
		botanyPollen
	];

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/rigs/caustic.ts
	const caustic = familyOf("caustic", {
		motes: {
			...MOTES,
			driftY: -.004,
			swirl: .03,
			swirlPeriod: 12
		},
		region: {
			cx: .5,
			cy: .46,
			w: .72,
			h: .46
		}
	});
	const sparks$4 = (count = 10) => ({ aspect, random }) => scatteredSparks({
		aspect,
		count,
		random,
		delay: .3
	});
	const softTop = (overrides = {}) => shaft({
		spread: .22,
		width: .2,
		length: 1.4,
		softness: .8,
		intensity: .8,
		streaks: .3,
		core: .3,
		...overrides
	});
	const water = (spec = {}) => ({ caustic: {
		scale: 2.2,
		speed: .35,
		inBeam: .8,
		...spec
	} });
	/** 池底（q）：顶光落进水里，光柱里是流动的焦散网，池底有一片焦散光斑。 */
	const causticPool = caustic({
		kind: "caustic-pool",
		label: "池底",
		mood: "quiet",
		light: rig([softTop()], {
			density: .8,
			driftY: -.015,
			ambient: .02
		}, {
			...GLARE,
			intensity: .6
		}, water({ floor: {
			cx: .5,
			cy: .82,
			rx: .42,
			ry: .14,
			strength: .7
		} })),
		lineArt: ({ aspect, random }) => ({
			paths: [{
				points: ellipse(aspect * .5, .82, aspect * .42, .14),
				width: .0014,
				alpha: .4,
				delay: .1,
				span: .6
			}],
			nodes: sparks$4()({
				aspect,
				random
			}).nodes
		}),
		typography: "horizontal",
		decay: {
			strength: .7,
			delay: 1.4
		}
	});
	/** 杯影（n）：侧光穿过玻璃杯，桌面上投下一片焦散。 */
	const causticCup = caustic({
		kind: "caustic-cup",
		label: "杯影",
		mood: "neutral",
		light: rig([shaft({
			x: -.05,
			y: .3,
			angle: .35,
			spread: .08,
			width: .18,
			length: 1.6,
			softness: .5,
			intensity: .9,
			streaks: .3,
			core: .4,
			sway: void 0
		})], { density: .8 }, null, water({
			scale: 3.2,
			speed: .25,
			inBeam: .3,
			floor: {
				cx: .36,
				cy: .8,
				rx: .14,
				ry: .07,
				strength: 1.1
			}
		})),
		lineArt: (context) => merge(glassCup(context.aspect * .24, .52, .1, .2), sparks$4()(context)),
		region: {
			cx: .62,
			cy: .48,
			w: .56,
			h: .64
		},
		typography: "vertical",
		decay: {
			strength: 1,
			delay: 1
		}
	});
	/** 涟漪（n）：水面荡开一圈圈同心波纹，光里的焦散随之晃动。 */
	const causticRipple = caustic({
		kind: "caustic-ripple",
		label: "涟漪",
		mood: "neutral",
		light: rig([softTop({ intensity: .75 })], { density: .8 }, {
			...GLARE,
			intensity: .5
		}, water({
			scale: 3,
			speed: .5,
			inBeam: .6,
			floor: {
				cx: .5,
				cy: .8,
				rx: .34,
				ry: .1,
				strength: .5
			}
		})),
		lineArt: (context) => merge(ripples(context.aspect * .5, .8, .34, 5), sparks$4()(context)),
		typography: "crossed",
		decay: {
			strength: 1,
			delay: 1
		}
	});
	/** 水面（l）：从水下仰望，水面的光斑与几道斜射下来的光束。 */
	const causticSurface = caustic({
		kind: "caustic-surface",
		label: "水面",
		mood: "loud",
		light: rig([
			.2,
			.42,
			.6,
			.8
		].map((x, i) => shaft({
			x,
			y: .1,
			angle: 1.35 + i * .06,
			spread: .04,
			width: .05,
			length: 1.2,
			softness: .6,
			intensity: .7,
			streaks: .5,
			streakFreq: 6,
			core: .5,
			sway: {
				amplitude: .05,
				period: 6 + i,
				phase: i * .2
			}
		})), {
			density: 1.1,
			driftY: -.02
		}, null, water({
			scale: 2.6,
			speed: .6,
			inBeam: .9,
			floor: {
				cx: .5,
				cy: .06,
				rx: .6,
				ry: .07,
				strength: .9
			}
		})),
		lineArt: (context) => merge(waterline(context.aspect, .1, .012, 7), sparks$4(14)(context)),
		motes: {
			...MOTES,
			count: 300,
			driftY: -.02,
			swirl: .03
		},
		region: {
			cx: .5,
			cy: .58,
			w: .72,
			h: .46
		},
		typography: "horizontal",
		decay: {
			strength: 1.2,
			delay: .8
		}
	});
	/** 戒光（q）：一个圆环的内壁，焦散曲线落在环里。 */
	const causticRing = caustic({
		kind: "caustic-ring",
		label: "戒光",
		mood: "quiet",
		light: rig([softTop({
			spread: .14,
			width: .1,
			intensity: .7
		})], { density: .7 }, {
			...GLARE,
			intensity: .5
		}, water({
			scale: 4,
			speed: .2,
			inBeam: .2,
			floor: {
				cx: .5,
				cy: .5,
				rx: .2,
				ry: .36,
				strength: .6
			}
		})),
		lineArt: (context) => merge({
			paths: [circle(context.aspect * .5, .5, .36, {
				width: .0024,
				alpha: .6
			}), circle(context.aspect * .5, .5, .32, {
				width: 9e-4,
				alpha: .3,
				delay: .1
			})],
			nodes: []
		}, sparks$4()(context)),
		region: {
			cx: .5,
			cy: .5,
			w: .4,
			h: .62
		},
		typography: "vertical",
		decay: {
			strength: .6,
			delay: 1.6
		}
	});
	/** 聚点（l）：放大镜把光聚成一点，正好落在字上（会聚光束过焦点再散开）。 */
	const causticBurn = caustic({
		kind: "caustic-burn",
		label: "聚点",
		mood: "loud",
		light: rig([shaft({
			x: .5,
			y: .2,
			angle: Math.PI / 2,
			spread: -.22,
			width: .2,
			length: 1.4,
			softness: .5,
			intensity: 1.2,
			streaks: .3,
			streakFreq: 5,
			core: .6,
			sway: {
				amplitude: .02,
				period: 9,
				phase: 0
			}
		}), shaft({
			x: .5,
			y: -.1,
			angle: Math.PI / 2,
			spread: .03,
			width: .22,
			length: 1,
			intensity: .4,
			streaks: .4,
			reach: .3,
			sway: void 0
		})], { density: 1 }, {
			x: .5,
			y: .65,
			radius: .05,
			intensity: 1.1,
			streak: .8
		}),
		lineArt: (context) => merge(magnifier(context.aspect * .5, .2, .1), sparks$4(12)(context)),
		region: {
			cx: .5,
			cy: .66,
			w: .72,
			h: .4
		},
		typography: "crossed",
		burst: {
			mode: "sung",
			density: .35,
			every: 1,
			offset: 0,
			size: 3.6,
			tint: [
				1,
				.7,
				.45
			]
		},
		decay: {
			strength: 1.3,
			delay: .7
		}
	});
	/** 粼粼（n）：横向的水面反光条纹在字上闪烁。 */
	const causticShimmer = caustic({
		kind: "caustic-shimmer",
		label: "粼粼",
		mood: "neutral",
		light: rig([shaft({
			x: -.05,
			y: .5,
			angle: .02,
			spread: .06,
			width: .5,
			length: 2.4,
			softness: .4,
			intensity: .8,
			streaks: .2,
			core: .2,
			gobo: {
				pattern: "blinds",
				frequency: 18,
				duty: .35,
				drift: .3
			},
			sway: void 0
		})], { density: .8 }, null, water({
			scale: 1.4,
			speed: .8,
			inBeam: .6
		})),
		lineArt: (context) => merge(waterline(context.aspect, .86, .008, 9), sparks$4()(context)),
		region: {
			cx: .5,
			cy: .5,
			w: .72,
			h: .5
		},
		typography: "horizontal",
		decay: {
			strength: 1,
			delay: 1
		}
	});
	/** 晶影（n）：光穿过水晶，投下放射的光斑与一点彩色。 */
	const causticCrystal = caustic({
		kind: "caustic-crystal",
		label: "晶影",
		mood: "neutral",
		light: rig([
			0,
			1,
			2,
			3,
			4
		].map((k) => shaft({
			x: .5,
			y: .26,
			angle: Math.PI / 2 + (k - 2) * .45,
			spread: .05,
			width: .01,
			length: .8,
			softness: .5,
			intensity: .6,
			streaks: .3,
			core: .4,
			spectrum: .7,
			sway: {
				amplitude: .05,
				period: 12,
				phase: k * .15
			}
		})), { density: .9 }, {
			...GLARE,
			x: .5,
			y: .26,
			radius: .07,
			intensity: 1,
			streak: .5
		}, water({
			scale: 3.4,
			speed: .2,
			inBeam: .4,
			floor: {
				cx: .5,
				cy: .8,
				rx: .3,
				ry: .09,
				strength: .5
			}
		})),
		lineArt: (context) => merge(crystal(context.aspect * .5, .26, .05), sparks$4(14)(context)),
		region: {
			cx: .5,
			cy: .58,
			w: .72,
			h: .44
		},
		typography: "crossed",
		decay: {
			strength: 1,
			delay: 1
		}
	});
	/** 漂流（q）：大尺度的焦散缓慢漂移，几乎静止。 */
	const causticDrift = caustic({
		kind: "caustic-drift",
		label: "漂流",
		mood: "quiet",
		light: rig([softTop({
			spread: .4,
			width: .4,
			intensity: 1,
			softness: .95
		})], {
			density: .8,
			driftX: .004,
			driftY: 0,
			ambient: .03
		}, {
			...GLARE,
			intensity: .5
		}, water({
			scale: 1,
			speed: .08,
			inBeam: .8,
			floor: {
				cx: .5,
				cy: .5,
				rx: .5,
				ry: .4,
				strength: .4
			}
		})),
		lineArt: sparks$4(8),
		region: {
			cx: .5,
			cy: .5,
			w: .72,
			h: .62
		},
		typography: "vertical",
		decay: {
			strength: .5,
			delay: 1.8
		}
	});
	/** 骤光（l）：高频闪烁的焦散随鼓点跳动。 */
	const causticStorm = caustic({
		kind: "caustic-storm",
		label: "骤光",
		mood: "loud",
		light: rig([softTop({
			intensity: 1,
			pulse: {
				amplitude: .35,
				period: .9,
				phase: 0
			}
		})], { density: 1.1 }, {
			...GLARE,
			intensity: .8
		}, water({
			scale: 3,
			speed: 1.3,
			inBeam: 1,
			floor: {
				cx: .5,
				cy: .82,
				rx: .5,
				ry: .14,
				strength: .8
			}
		})),
		lineArt: (context) => merge(ripples(context.aspect * .5, .82, .5, 4), sparks$4(14)(context)),
		typography: "horizontal",
		decay: {
			strength: 1.3,
			delay: .7
		}
	});
	const CAUSTIC_PROFILES = [
		causticPool,
		causticCup,
		causticRipple,
		causticSurface,
		causticRing,
		causticBurn,
		causticShimmer,
		causticCrystal,
		causticDrift,
		causticStorm
	];

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/rigs/lattice.ts
	const lattice = familyOf("lattice", {
		region: {
			cx: .56,
			cy: .58,
			w: .66,
			h: .5
		},
		motes: {
			...MOTES,
			driftX: .008,
			driftY: -.006
		}
	});
	/** 从左上的窗斜射进来的宽光（窗光的公共形状）。 */
	const windowBeam = (overrides = {}) => shaft({
		x: .1,
		y: .06,
		angle: .62,
		spread: .1,
		width: .34,
		length: 1.3,
		softness: .35,
		intensity: 1,
		streaks: .25,
		streakFreq: 4,
		core: .3,
		sway: {
			amplitude: .01,
			period: 17,
			phase: 0
		},
		...overrides
	});
	const sparks$3 = (region) => ({ aspect, random }) => scatteredSparks({
		aspect,
		count: 14,
		random,
		delay: .3,
		region: region ?? [
			.3,
			.3,
			.95,
			.9
		]
	});
	/** 百叶（n）：左上的窗透进一道宽斜光，百叶把它切成一条条平行的光带。 */
	const latticeBlinds = lattice({
		kind: "lattice-blinds",
		label: "百叶",
		mood: "neutral",
		light: rig([windowBeam({ gobo: {
			pattern: "blinds",
			frequency: 13,
			duty: .52,
			drift: .02
		} })], {
			driftX: .012,
			driftY: -.02
		}, {
			...GLARE,
			x: .1,
			y: .06,
			radius: .16,
			intensity: .8
		}),
		lineArt: (context) => mergeSpecs(blindsWindow({
			x: context.aspect * .03,
			y: .02,
			w: context.aspect * .16,
			h: .26,
			slats: 9,
			alpha: .5
		}), sparks$3()(context)),
		typography: "horizontal",
		decay: {
			strength: 1,
			delay: 1
		}
	});
	/** 十字窗（q）：右上的十字窗投下四块光斑，中间一道窗棂的影子。 */
	const latticeCross = lattice({
		kind: "lattice-cross",
		label: "十字窗",
		mood: "quiet",
		light: rig([windowBeam({
			x: .9,
			angle: Math.PI - .7,
			width: .3,
			intensity: .9,
			gobo: {
				pattern: "cross",
				frequency: 0,
				duty: .16,
				drift: 0
			}
		})], { driftX: -.008 }, {
			...GLARE,
			x: .9,
			y: .06,
			radius: .14,
			intensity: .7
		}),
		lineArt: (context) => mergeSpecs(crossWindow(context.aspect * .8, .01, context.aspect * .16, .26), sparks$3([
			.05,
			.3,
			.7,
			.9
		])(context)),
		region: {
			cx: .4,
			cy: .52,
			w: .6,
			h: .62
		},
		typography: "vertical",
		decay: {
			strength: .7,
			delay: 1.4
		}
	});
	/** 拱窗（l）：高高的拱窗斜照下一道长长的丁达尔光束。 */
	const latticeArch = lattice({
		kind: "lattice-arch",
		label: "拱窗",
		mood: "loud",
		light: rig([windowBeam({
			x: .14,
			y: -.02,
			angle: .95,
			width: .14,
			spread: .06,
			length: 1.6,
			intensity: 1.3,
			streaks: .55,
			streakFreq: 8,
			core: .6
		}), windowBeam({
			x: .14,
			y: -.02,
			angle: .95,
			width: .3,
			spread: .2,
			length: .9,
			softness: .9,
			intensity: .3,
			streaks: .8,
			streakFreq: 22
		})], { density: 1.1 }, {
			...GLARE,
			x: .14,
			y: 0,
			radius: .18,
			intensity: 1.1,
			streak: .6
		}),
		lineArt: (context) => mergeSpecs(archWindow$1(context.aspect * .08, -.06, context.aspect * .12, .32), sparks$3()(context)),
		typography: "crossed",
		decay: {
			strength: 1.2,
			delay: .8
		},
		starfall: {
			...STARFALL,
			rain: 90,
			rainSpread: .8
		}
	});
	/** 玫瑰窗（l）：顶上一扇圆花窗，放射出一扇细光束。 */
	const latticeRose = lattice({
		kind: "lattice-rose",
		label: "玫瑰窗",
		mood: "loud",
		light: rig([
			-2,
			-1,
			0,
			1,
			2,
			3
		].map((k) => shaft({
			y: .08,
			angle: Math.PI / 2 + (k - .5) * .2,
			spread: .035,
			width: .03,
			length: 1,
			intensity: .65 - Math.abs(k - .5) * .06,
			streaks: .4,
			streakFreq: 5,
			sway: {
				amplitude: .015,
				period: 13,
				phase: k * .1
			}
		})), {}, {
			...GLARE,
			y: .08,
			radius: .16,
			intensity: 1.1,
			streak: .5
		}),
		lineArt: ({ aspect, random }) => mergeSpecs(roseWindow(aspect * .5, .08, .12), scatteredSparks({
			aspect,
			count: 16,
			random,
			delay: .3
		})),
		region: {
			cx: .5,
			cy: .6,
			w: .72,
			h: .46
		},
		typography: "horizontal",
		decay: {
			strength: 1.2,
			delay: .8
		},
		starfall: STARFALL
	});
	/** 门缝（q）：左边一道门缝，极窄的光横着划过画面。 */
	const latticeSlit = lattice({
		kind: "lattice-slit",
		label: "门缝",
		mood: "quiet",
		light: rig([shaft({
			x: .1,
			y: .42,
			angle: .06,
			spread: .02,
			width: .012,
			length: 2.4,
			intensity: 1,
			streaks: .3,
			streakFreq: 3,
			core: .8,
			sway: {
				amplitude: .01,
				period: 15,
				phase: 0
			}
		})], {
			density: .8,
			ambient: .015
		}, {
			...GLARE,
			x: .1,
			y: .42,
			radius: .08,
			intensity: .8,
			streak: .9
		}),
		lineArt: ({ aspect, random }) => mergeSpecs(doorSlit(aspect * .1, .08, .86, .012), scatteredSparks({
			aspect,
			count: 10,
			random,
			delay: .3,
			region: [
				.2,
				.2,
				.95,
				.9
			]
		})),
		region: {
			cx: .55,
			cy: .5,
			w: .6,
			h: .64
		},
		typography: "vertical",
		decay: {
			strength: .6,
			delay: 1.6
		}
	});
	/** 障子（q）：方格纸窗透进柔和的漫射光，窗格的细影子落在光里。 */
	const latticeShoji = lattice({
		kind: "lattice-shoji",
		label: "障子",
		mood: "quiet",
		light: rig([windowBeam({
			x: .12,
			y: .1,
			angle: .4,
			width: .5,
			spread: .18,
			softness: .95,
			intensity: .7,
			streaks: .1,
			core: .2,
			gobo: {
				pattern: "blinds",
				frequency: 10,
				duty: .86,
				drift: 0
			}
		})], {
			density: .7,
			tyndallBase: .25
		}, null),
		lineArt: ({ aspect, random }) => mergeSpecs(gridPanel(aspect * .02, .06, aspect * .18, .4, 3, 5), scatteredSparks({
			aspect,
			count: 10,
			random,
			delay: .3
		})),
		typography: "horizontal",
		decay: {
			strength: .6,
			delay: 1.6
		}
	});
	/** 格栅（n）：菱形格栅的光影在字上流动。 */
	const latticeGrille = lattice({
		kind: "lattice-grille",
		label: "格栅",
		mood: "neutral",
		light: rig([windowBeam({
			width: .4,
			spread: .12,
			intensity: 1,
			gobo: {
				pattern: "lattice",
				frequency: 9,
				duty: .62,
				drift: .04
			}
		})], { driftX: .01 }, {
			...GLARE,
			x: .1,
			y: .06,
			radius: .14,
			intensity: .7
		}),
		lineArt: (context) => mergeSpecs(gridPanel(context.aspect * .02, .02, context.aspect * .16, .26, 4, 4, true), sparks$3()(context)),
		typography: "crossed",
		decay: {
			strength: 1,
			delay: 1
		}
	});
	/** 扫窗（n）：光源在动，百叶的影子扫过整句。 */
	const latticeSweep = lattice({
		kind: "lattice-sweep",
		label: "扫窗",
		mood: "neutral",
		light: rig([windowBeam({
			angle: .75,
			width: .3,
			intensity: 1,
			sway: {
				amplitude: .32,
				period: 10,
				phase: 0
			},
			gobo: {
				pattern: "blinds",
				frequency: 11,
				duty: .55,
				drift: .05
			}
		})], { driftX: .01 }, {
			...GLARE,
			x: .1,
			y: .06,
			radius: .14,
			intensity: .7
		}),
		lineArt: (context) => mergeSpecs(blindsWindow({
			x: context.aspect * .03,
			y: .02,
			w: context.aspect * .16,
			h: .26,
			slats: 8,
			alpha: .45
		}), sparks$3()(context)),
		typography: "horizontal",
		decay: {
			strength: 1,
			delay: 1
		},
		camera: {
			push: .02,
			driftX: .012,
			driftY: 0
		}
	});
	/** 叶隙（q）：树叶缝隙里漏下来的斑驳光，随风摇曳。 */
	const latticeKomorebi = lattice({
		kind: "lattice-komorebi",
		label: "叶隙",
		mood: "quiet",
		light: rig([shaft({
			x: .35,
			y: -.05,
			angle: 1.35,
			spread: .2,
			width: .2,
			length: 1.3,
			softness: .7,
			intensity: 1,
			streaks: .2,
			core: .3,
			gobo: {
				pattern: "leaves",
				frequency: 7,
				duty: .52,
				drift: .12
			},
			sway: {
				amplitude: .03,
				period: 7,
				phase: 0
			}
		}), shaft({
			x: .72,
			y: -.05,
			angle: 1.8,
			spread: .14,
			width: .12,
			length: 1.1,
			softness: .7,
			intensity: .7,
			streaks: .2,
			core: .3,
			gobo: {
				pattern: "leaves",
				frequency: 8,
				duty: .55,
				drift: .1
			},
			sway: {
				amplitude: .03,
				period: 8,
				phase: .4
			}
		})], {
			density: .9,
			driftX: .01
		}, null),
		lineArt: ({ aspect, random }) => merge(canopy(aspect, random), scatteredSparks({
			aspect,
			count: 12,
			random,
			delay: .3
		})),
		motes: {
			...MOTES,
			count: 220,
			driftX: .01,
			driftY: .004,
			swirl: .03
		},
		region: {
			cx: .5,
			cy: .54,
			w: .72,
			h: .6
		},
		typography: "vertical",
		decay: {
			strength: .8,
			delay: 1.2
		}
	});
	/** 殿堂（l）：一排高窗照下平行的斜光，层层烟雾。 */
	const latticeNave = lattice({
		kind: "lattice-nave",
		label: "殿堂",
		mood: "loud",
		light: rig([
			.12,
			.34,
			.56,
			.78
		].map((x, i) => windowBeam({
			x,
			y: -.02,
			angle: 1.1,
			width: .06,
			spread: .05,
			length: 1.5,
			softness: .5,
			intensity: .75,
			streaks: .5,
			streakFreq: 6,
			core: .5,
			sway: {
				amplitude: .008,
				period: 19,
				phase: i * .2
			}
		})), {
			density: 1.2,
			driftX: .006,
			driftY: -.03
		}, {
			...GLARE,
			x: .45,
			y: 0,
			radius: .25,
			intensity: .6,
			streak: .3
		}),
		lineArt: ({ aspect, random }) => mergeSpecs(tallWindows(aspect * .05, aspect * .95, -.08, .26, 4), scatteredSparks({
			aspect,
			count: 18,
			random,
			delay: .3
		})),
		typography: "crossed",
		decay: {
			strength: 1.2,
			delay: .8
		},
		starfall: {
			...STARFALL,
			rain: 80,
			rainSpread: 1.2
		}
	});
	const LATTICE_PROFILES = [
		latticeBlinds,
		latticeCross,
		latticeArch,
		latticeRose,
		latticeSlit,
		latticeShoji,
		latticeGrille,
		latticeSweep,
		latticeKomorebi,
		latticeNave
	];

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/rigs/motes.ts
	const motesFamily = familyOf("motes", {
		starfall: {
			...STARFALL,
			rain: 30
		},
		lineArt: ({ aspect, random }) => scatteredSparks({
			aspect,
			count: 10,
			random,
			delay: .3
		})
	});
	const particles = (overrides) => ({
		...MOTES,
		...overrides
	});
	/** 浮尘（q）：暗室里一束斜光，光里的尘埃缓缓旋转。 */
	const motesDust = motesFamily({
		kind: "motes-dust",
		label: "浮尘",
		mood: "quiet",
		light: rig([shaft({
			x: .15,
			y: -.05,
			angle: 1.05,
			spread: .1,
			width: .1,
			length: 1.5,
			intensity: .8,
			streaks: .4
		})], {
			density: .8,
			ambient: .015
		}, null),
		motes: particles({
			count: 420,
			sizeMin: .003,
			sizeMax: .012,
			driftX: .002,
			driftY: -.004,
			swirl: .035,
			swirlPeriod: 14,
			gain: 1.6
		}),
		typography: "horizontal",
		decay: {
			strength: .7,
			delay: 1.4
		}
	});
	/** 萤火（q）：没有光束，只有散落的萤光点，自己亮着、忽明忽暗。 */
	const motesFirefly = motesFamily({
		kind: "motes-firefly",
		label: "萤火",
		mood: "quiet",
		light: rig([shaft({
			spread: .05,
			intensity: .15
		})], {
			density: .6,
			ambient: .02
		}, null),
		motes: particles({
			count: 90,
			sizeMin: .008,
			sizeMax: .022,
			driftX: .003,
			driftY: -.003,
			swirl: .06,
			swirlPeriod: 11,
			twinkle: .9,
			ambient: .55,
			gain: .8
		}),
		front: {
			...FRONT,
			count: 14,
			ambient: .12,
			twinkle: .7
		},
		region: {
			cx: .5,
			cy: .5,
			w: .5,
			h: .62
		},
		typography: "vertical",
		decay: {
			strength: .6,
			delay: 1.6
		}
	});
	/** 烟缕（n）：一缕缕上升的烟，被侧光勾出轮廓。 */
	const motesSmoke = motesFamily({
		kind: "motes-smoke",
		label: "烟缕",
		mood: "neutral",
		light: rig([shaft({
			x: -.05,
			y: .55,
			angle: -.08,
			spread: .18,
			width: .3,
			length: 1.6,
			softness: .7,
			intensity: .42,
			streaks: .2,
			core: .3,
			sway: void 0
		})], {
			density: 1.5,
			tyndallBase: .05,
			scale: 1.8,
			warp: 1.8,
			driftX: .004,
			driftY: -.06,
			ambient: .05
		}, null),
		motes: particles({ count: 140 }),
		typography: "crossed",
		decay: {
			strength: 1,
			delay: 1
		}
	});
	/** 飞烬（l）：暖红的光从下面烧上来，火星往上飘。 */
	const motesEmbers = motesFamily({
		kind: "motes-embers",
		label: "飞烬",
		mood: "loud",
		light: rig([shaft({
			x: .5,
			y: 1.05,
			angle: -Math.PI / 2,
			spread: .35,
			width: .3,
			length: .6,
			softness: .9,
			intensity: .8,
			streaks: .6,
			streakFreq: 8,
			core: .3,
			tint: [
				1,
				.55,
				.3
			],
			pulse: {
				amplitude: .2,
				period: 1.3,
				phase: 0
			},
			sway: void 0
		})], {
			density: 1.1,
			driftY: -.05
		}, null),
		motes: particles({
			count: 380,
			sizeMin: .003,
			sizeMax: .01,
			driftX: .004,
			driftY: -.07,
			swirl: .03,
			swirlPeriod: 5,
			twinkle: .8,
			ambient: .1,
			gain: 1.8
		}),
		region: {
			cx: .5,
			cy: .44,
			w: .72,
			h: .46
		},
		typography: "horizontal",
		decay: {
			strength: 1.4,
			delay: .6
		}
	});
	/** 光雪（q）：光点像雪一样缓缓落下。 */
	const motesSnow = motesFamily({
		kind: "motes-snow",
		label: "光雪",
		mood: "quiet",
		light: rig([shaft({
			spread: .2,
			width: .2,
			softness: .9,
			intensity: .5,
			core: .3
		})], {
			density: .8,
			driftY: .01
		}, null),
		motes: particles({
			count: 460,
			sizeMin: .003,
			sizeMax: .012,
			driftX: .003,
			driftY: .035,
			swirl: .02,
			swirlPeriod: 8,
			twinkle: .3,
			ambient: .15,
			gain: 1
		}),
		region: {
			cx: .5,
			cy: .5,
			w: .5,
			h: .62
		},
		typography: "vertical",
		decay: {
			strength: .6,
			delay: 1.6
		},
		starfall: {
			...STARFALL,
			rain: 60,
			rainSpeed: .12,
			rainSpread: 1.8,
			brightness: .7
		}
	});
	/** 散景（n）：前景一片片大光斑虚化，焦点在字上。 */
	const motesBokeh = motesFamily({
		kind: "motes-bokeh",
		label: "散景",
		mood: "neutral",
		light: rig([shaft({ intensity: .6 })], { density: .8 }, null),
		motes: particles({ count: 120 }),
		front: {
			...FRONT,
			count: 34,
			sizeMin: .06,
			sizeMax: .2,
			ambient: .1,
			gain: .5,
			twinkle: .3,
			driftX: .008
		},
		typography: "horizontal",
		decay: {
			strength: 1,
			delay: 1
		}
	});
	/** 晨雾（q）：低伏的雾层，光从雾后面透出来。 */
	const motesMist = motesFamily({
		kind: "motes-mist",
		label: "晨雾",
		mood: "quiet",
		light: rig([shaft({
			x: .7,
			y: .62,
			angle: -2.8,
			spread: .5,
			width: .3,
			length: 1.2,
			softness: 1,
			intensity: .55,
			streaks: .3,
			core: .2,
			sway: void 0
		})], {
			density: 1.5,
			tyndallBase: .1,
			scale: 1.2,
			warp: .6,
			driftX: .02,
			driftY: 0,
			ambient: .05
		}, {
			x: .7,
			y: .62,
			radius: .16,
			intensity: .5,
			streak: .08
		}),
		lineArt: ({ aspect, random }) => merge(horizon(aspect, .64), scatteredSparks({
			aspect,
			count: 8,
			random,
			delay: .3,
			region: [
				.05,
				.05,
				.95,
				.55
			]
		})),
		motes: particles({
			count: 120,
			driftX: .01
		}),
		region: {
			cx: .36,
			cy: .42,
			w: .44,
			h: .5
		},
		typography: "vertical",
		decay: {
			strength: .6,
			delay: 1.6
		}
	});
	/** 光群（l）：粒子成群地绕着字流动，又散开。 */
	const motesSwarm = motesFamily({
		kind: "motes-swarm",
		label: "光群",
		mood: "loud",
		light: rig([shaft({ intensity: .8 }), shaft({
			spread: .3,
			length: .6,
			softness: .9,
			intensity: .25,
			streaks: .8,
			streakFreq: 24
		})]),
		motes: particles({
			count: 460,
			sizeMin: .003,
			sizeMax: .012,
			driftX: 0,
			driftY: 0,
			swirl: .14,
			swirlPeriod: 6,
			twinkle: .6,
			ambient: .08,
			gain: 1.6
		}),
		typography: "crossed",
		decay: {
			strength: 1.3,
			delay: .7
		}
	});
	/** 化尘（n）：字一唱完就碎成光尘飘散（衔接用）。 */
	const motesDissolve = motesFamily({
		kind: "motes-dissolve",
		label: "化尘",
		mood: "neutral",
		light: rig([shaft({ intensity: .7 })]),
		motes: particles({
			count: 380,
			driftY: -.02,
			swirl: .04,
			gain: 1.5
		}),
		typography: "crossed",
		decay: {
			strength: 2.2,
			delay: .3
		}
	});
	/** 极光（l）：画面上方帘子一样流动的光幕。 */
	const motesAurora = motesFamily({
		kind: "motes-aurora",
		label: "极光",
		mood: "loud",
		light: rig([
			.2,
			.4,
			.6,
			.8
		].map((x, i) => shaft({
			x,
			y: -.1,
			angle: Math.PI / 2 + .1,
			spread: .06,
			width: .16,
			length: .7,
			softness: .8,
			intensity: .7,
			streaks: .9,
			streakFreq: 30,
			streakSpeed: .3,
			core: .2,
			spectrum: .5,
			tint: [
				.6,
				1,
				.8
			],
			sway: {
				amplitude: .12,
				period: 8 + i * 1.7,
				phase: i * .2
			}
		})), {
			density: .9,
			ambient: .03
		}, null),
		region: {
			cx: .5,
			cy: .62,
			w: .72,
			h: .4
		},
		typography: "horizontal",
		decay: {
			strength: 1.2,
			delay: .8
		},
		starfall: {
			...STARFALL,
			stars: 560,
			rain: 20
		}
	});
	const MOTES_PROFILES = [
		motesDust,
		motesFirefly,
		motesSmoke,
		motesEmbers,
		motesSnow,
		motesBokeh,
		motesMist,
		motesSwarm,
		motesDissolve,
		motesAurora
	];

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/rigs/optics.ts
	const optics = familyOf("optics", {
		region: {
			cx: .5,
			cy: .74,
			w: .72,
			h: .3
		},
		heroSize: .075,
		motes: {
			...MOTES,
			count: 180
		}
	});
	/** 从 a 到 b（画面比例）的一段光线：细光束，射程正好到 b（b 为 null 时一直延伸）。 */
	const ray = (aspect, a, b, overrides = {}, open = false) => {
		const dx = (b[0] - a[0]) * aspect;
		const dy = b[1] - a[1];
		return {
			x: a[0],
			y: a[1],
			angle: Math.atan2(dy, dx),
			spread: .004,
			width: .008,
			length: 3,
			softness: .6,
			intensity: .9,
			streaks: .1,
			streakFreq: 3,
			streakSpeed: .05,
			core: .8,
			...open ? {} : { reach: Math.hypot(dx, dy) + .02 },
			...overrides
		};
	};
	/** 同一条路径的线稿（画面比例 → 高度单位）。 */
	const rayArt = (aspect, points, delay = .2) => rayPath(points.map(([x, y]) => [x * aspect, y]), { delay });
	const opticsRig = (beams, glare = null) => ({
		beams,
		fog: {
			...FOG,
			density: .9,
			tyndallBase: .2,
			ambient: .02
		},
		glare
	});
	const sparks$2 = ({ aspect, random }) => scatteredSparks({
		aspect,
		count: 10,
		random,
		delay: .3,
		region: [
			.05,
			.05,
			.95,
			.6
		]
	});
	const AXIS = .4;
	/** 会聚（n）：平行光经凸透镜会聚到焦点。 */
	const opticsConvex = optics({
		kind: "optics-convex",
		label: "会聚",
		mood: "neutral",
		light: ({ aspect }) => opticsRig([
			.3,
			.4,
			.5
		].flatMap((y) => [ray(aspect, [-.02, y], [.42, y]), ray(aspect, [.42, y], [.7, AXIS], {}, true)]), {
			x: .7,
			y: AXIS,
			radius: .05,
			intensity: .9,
			streak: .6
		}),
		lineArt: (context) => merge(lens(context.aspect * .42, AXIS, .3, true, context.aspect), ...[
			.3,
			.4,
			.5
		].map((y, i) => rayArt(context.aspect, [
			[.02, y],
			[.42, y],
			[.7, AXIS],
			[.92, AXIS + (AXIS - y) * .8]
		], .2 + i * .08)), focusMark(context.aspect * .7, AXIS, { delay: .5 }), sparks$2(context)),
		typography: "horizontal",
		decay: {
			strength: 1,
			delay: 1
		}
	});
	/** 发散（q）：凹透镜把平行光打散，虚焦点在透镜前面（虚线）。 */
	const opticsConcave = optics({
		kind: "optics-concave",
		label: "发散",
		mood: "quiet",
		light: ({ aspect }) => opticsRig([
			.32,
			.4,
			.48
		].flatMap((y) => [ray(aspect, [-.02, y], [.42, y], { intensity: .7 }), ray(aspect, [.42, y], [.58, y + (y - AXIS) * 1.6], {
			intensity: .7,
			spread: .01
		}, true)])),
		lineArt: (context) => merge(lens(context.aspect * .42, AXIS, .28, false, context.aspect), ...[.32, .48].map((y, i) => merge(rayArt(context.aspect, [
			[.02, y],
			[.42, y],
			[.62, y + (y - AXIS) * 2.2]
		], .2 + i * .1), {
			paths: [segment([context.aspect * .42, y], [context.aspect * .26, AXIS], {
				width: 9e-4,
				alpha: .35,
				delay: .5,
				dash: [.005, .006]
			})],
			nodes: []
		})), focusMark(context.aspect * .26, AXIS, { delay: .6 }), sparks$2(context)),
		region: {
			cx: .72,
			cy: .56,
			w: .4,
			h: .72
		},
		typography: "vertical",
		decay: {
			strength: .7,
			delay: 1.4
		}
	});
	/** 反射（q）：光线打在平面镜上，入射角等于反射角，法线与角标。 */
	const opticsMirror = optics({
		kind: "optics-mirror",
		label: "反射",
		mood: "quiet",
		light: ({ aspect }) => opticsRig([ray(aspect, [.12, .08], [.5, .6], {
			width: .02,
			intensity: 1
		}), ray(aspect, [.5, .6], [.88, .08], {
			width: .02,
			intensity: .8
		}, true)], {
			x: .5,
			y: .6,
			radius: .05,
			intensity: .8,
			streak: .7
		}),
		lineArt: (context) => merge(mirror(context.aspect * .5, .6, .6, 0), rayArt(context.aspect, [
			[.12, .08],
			[.5, .6],
			[.88, .08]
		]), sparks$2(context)),
		region: {
			cx: .5,
			cy: .34,
			w: .5,
			h: .36
		},
		heroSize: .07,
		typography: "crossed",
		decay: {
			strength: .7,
			delay: 1.4
		}
	});
	/** 折射（n）：光线穿过介质界面向法线偏折。 */
	const opticsRefract = optics({
		kind: "optics-refract",
		label: "折射",
		mood: "neutral",
		light: ({ aspect }) => opticsRig([
			ray(aspect, [.18, .05], [.46, .5], {
				width: .02,
				intensity: 1
			}),
			ray(aspect, [.46, .5], [.58, 1.05], {
				width: .02,
				intensity: .8
			}, true),
			ray(aspect, [.46, .5], [.74, .05], {
				width: .01,
				intensity: .3
			}, true)
		], {
			x: .46,
			y: .5,
			radius: .05,
			intensity: .8,
			streak: .8
		}),
		lineArt: (context) => merge(interfaceLine(context.aspect, .5), rayArt(context.aspect, [
			[.18, .05],
			[.46, .5],
			[.58, .98]
		]), {
			paths: [segment([context.aspect * .46, .18], [context.aspect * .46, .82], {
				width: 9e-4,
				alpha: .35,
				delay: .4,
				dash: [.006, .006]
			})],
			nodes: []
		}, sparks$2(context)),
		region: {
			cx: .72,
			cy: .3,
			w: .44,
			h: .3
		},
		heroSize: .07,
		typography: "horizontal",
		decay: {
			strength: 1,
			delay: 1
		}
	});
	/** 光具座（n）：横向光学台上一串透镜与刻度尺，一束光依次穿过。 */
	const opticsBench = optics({
		kind: "optics-bench",
		label: "光具座",
		mood: "neutral",
		light: ({ aspect }) => opticsRig([
			ray(aspect, [-.02, AXIS], [.3, AXIS], {
				width: .05,
				spread: .01,
				intensity: .8
			}),
			{
				...ray(aspect, [.3, AXIS], [.5, AXIS], {
					width: .05,
					intensity: .9
				}),
				spread: -.06
			},
			{
				...ray(aspect, [.5, AXIS], [.7, AXIS], {
					width: .02,
					intensity: .9
				}),
				spread: .06
			},
			ray(aspect, [.7, AXIS], [1.02, AXIS], {
				width: .05,
				intensity: .7
			}, true)
		], {
			x: .5,
			y: AXIS,
			radius: .04,
			intensity: .7,
			streak: .6
		}),
		lineArt: (context) => merge(...[
			.3,
			.5,
			.7
		].map((x, i) => lens(context.aspect * x, AXIS, .16 + i % 2 * .04, i !== 1, context.aspect, { delay: i * .1 })), ruler(context.aspect * .06, context.aspect * .94, .58, 40, { delay: .3 }), sparks$2(context)),
		typography: "horizontal",
		decay: {
			strength: 1,
			delay: 1
		}
	});
	/** 潜望（n）：光线在两面镜之间折返，从上面拐到下面。 */
	const opticsPeriscope = optics({
		kind: "optics-periscope",
		label: "潜望",
		mood: "neutral",
		light: ({ aspect }) => opticsRig([
			ray(aspect, [1.02, .14], [.3, .14], { width: .02 }),
			ray(aspect, [.3, .14], [.3, .84], { width: .02 }),
			ray(aspect, [.3, .84], [1.02, .84], {
				width: .02,
				intensity: .8
			}, true)
		], {
			x: .3,
			y: .14,
			radius: .04,
			intensity: .7,
			streak: .4
		}),
		lineArt: (context) => merge(mirror(context.aspect * .3, .14, .14, Math.PI / 4), mirror(context.aspect * .3, .84, .14, Math.PI / 4, { delay: .1 }), rayArt(context.aspect, [
			[.98, .14],
			[.3, .14],
			[.3, .84],
			[.98, .84]
		]), sparks$2(context)),
		region: {
			cx: .62,
			cy: .49,
			w: .4,
			h: .5
		},
		typography: "vertical",
		decay: {
			strength: .8,
			delay: 1.2
		}
	});
	/** 光纤（l）：光在一条弯曲的光纤里全反射前进，从另一头喷出一锥光。 */
	const opticsFiber = optics({
		kind: "optics-fiber",
		label: "光纤",
		mood: "loud",
		light: () => opticsRig([{
			x: .84,
			y: .2,
			angle: -.5,
			spread: .3,
			width: .02,
			length: .9,
			softness: .6,
			intensity: 1.2,
			streaks: .5,
			streakFreq: 9,
			streakSpeed: .1,
			core: .6,
			pulse: {
				amplitude: .2,
				period: 2.3,
				phase: 0
			}
		}, {
			x: .84,
			y: .2,
			angle: -.5,
			spread: .08,
			width: .02,
			length: 1.2,
			softness: .5,
			intensity: .9,
			streaks: .2,
			streakFreq: 3,
			streakSpeed: 0,
			core: .8
		}], {
			x: .84,
			y: .2,
			radius: .07,
			intensity: 1.2,
			streak: .8
		}),
		lineArt: (context) => merge(fiber([
			[context.aspect * .04, .86],
			[context.aspect * .4, .95],
			[context.aspect * .5, .1],
			[context.aspect * .84, .2]
		], .012), sparks$2(context)),
		region: {
			cx: .46,
			cy: .52,
			w: .6,
			h: .5
		},
		typography: "crossed",
		burst: {
			mode: "sung",
			density: .3,
			every: 2,
			offset: 0,
			size: 3.2,
			tint: [
				.8,
				.9,
				1
			]
		},
		decay: {
			strength: 1.3,
			delay: .7
		},
		starfall: {
			stars: 380,
			opening: 3,
			rain: 60,
			rainSpeed: .3,
			rainSpread: .8,
			brightness: 1
		}
	});
	/** 暗箱（q）：一道光经小孔穿进暗箱（过孔会聚再散开），背面是倒立的像。 */
	const opticsPinhole = optics({
		kind: "optics-pinhole",
		label: "暗箱",
		mood: "quiet",
		light: ({ aspect }) => {
			const distance = (.45 + .02) * aspect;
			return opticsRig([{
				x: -.02,
				y: AXIS,
				angle: 0,
				spread: -Math.atan(.15 / distance),
				width: .3,
				length: 3,
				softness: .4,
				intensity: .8,
				streaks: .3,
				streakFreq: 6,
				streakSpeed: .05,
				core: .3,
				reach: distance + .3
			}], {
				x: .45,
				y: AXIS,
				radius: .03,
				intensity: 1,
				streak: .3
			});
		},
		lineArt: (context) => merge(pinholeBox(context.aspect * .45 + .15, AXIS, .3, .3), sparks$2(context)),
		region: {
			cx: .2,
			cy: .52,
			w: .3,
			h: .7
		},
		heroSize: .07,
		typography: "vertical",
		decay: {
			strength: .6,
			delay: 1.6
		}
	});
	/** 望远（l）：物镜把平行光聚到焦点，目镜再把它变成更细的平行光。 */
	const opticsTelescope = optics({
		kind: "optics-telescope",
		label: "望远",
		mood: "loud",
		light: ({ aspect }) => opticsRig([.3, .5].flatMap((y) => [
			ray(aspect, [-.02, y], [.28, y], { width: .02 }),
			ray(aspect, [.28, y], [.62, AXIS]),
			ray(aspect, [.72, AXIS + (y - AXIS) * .3], [1.02, AXIS + (y - AXIS) * .3], {
				width: .02,
				intensity: 1.1
			}, true)
		]), {
			x: .62,
			y: AXIS,
			radius: .05,
			intensity: 1,
			streak: .8
		}),
		lineArt: (context) => merge(lens(context.aspect * .28, AXIS, .36, true, context.aspect), lens(context.aspect * .72, AXIS, .12, true, context.aspect, { delay: .1 }), ...[.3, .5].map((y, i) => rayArt(context.aspect, [
			[.02, y],
			[.28, y],
			[.62, AXIS],
			[.72, AXIS - (y - AXIS) * .3],
			[.98, AXIS - (y - AXIS) * .3]
		], .2 + i * .1)), focusMark(context.aspect * .62, AXIS, { delay: .5 }), sparks$2(context)),
		typography: "horizontal",
		burst: {
			mode: "sung",
			density: .25,
			every: 2,
			offset: 1,
			size: 3.2,
			tint: [
				1,
				.8,
				.55
			]
		},
		decay: {
			strength: 1.2,
			delay: .8
		}
	});
	/** 追迹（l）：几条光线在画面里反复折射，织成一张网。 */
	const opticsRaytrace = optics({
		kind: "optics-raytrace",
		label: "追迹",
		mood: "loud",
		light: ({ aspect, random }) => {
			const points = Array.from({ length: 7 }, (_, i) => [.05 + i / 6 * .9, .1 + random() * .55]);
			return opticsRig(points.slice(0, 6).map((point, i) => ray(aspect, point, points[i + 1], {
				intensity: .8,
				width: .012
			})), {
				x: points[0][0],
				y: points[0][1],
				radius: .05,
				intensity: .9,
				streak: .6
			});
		},
		lineArt: ({ aspect, random }) => {
			const art = [];
			for (let k = 0; k < 4; k += 1) {
				const points = Array.from({ length: 6 }, (_, i) => [.05 + i / 5 * .9, .08 + random() * .6]);
				art.push(rayArt(aspect, points, .1 + k * .1));
			}
			return merge(...art, scatteredSparks({
				aspect,
				count: 16,
				random,
				delay: .3
			}));
		},
		typography: "crossed",
		decay: {
			strength: 1.3,
			delay: .7
		}
	});
	const OPTICS_PROFILES = [
		opticsConvex,
		opticsConcave,
		opticsMirror,
		opticsRefract,
		opticsBench,
		opticsPeriscope,
		opticsFiber,
		opticsPinhole,
		opticsTelescope,
		opticsRaytrace
	];

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/rigs/prism.ts
	const prism = familyOf("prism", { starfall: {
		...STARFALL,
		rain: 50
	} });
	const sparks$1 = ({ aspect, random }) => scatteredSparks({
		aspect,
		count: 16,
		random,
		delay: .3
	});
	/** 一道光谱色的细光束（谱线、合光用）。 */
	const tinted = (tint, overrides) => shaft({
		spread: .012,
		width: .012,
		length: 1.4,
		intensity: .7,
		streaks: .2,
		streakFreq: 3,
		core: .7,
		sway: void 0,
		tint,
		...overrides
	});
	/** 分光（l）：一束白光从左边射进三棱镜，出射成一扇光谱，字落在光谱上。 */
	const prismSplit = prism({
		kind: "prism-split",
		label: "分光",
		mood: "loud",
		light: rig([shaft({
			x: -.02,
			y: .36,
			angle: .08,
			spread: .01,
			width: .02,
			length: 3,
			intensity: .9,
			streaks: .2,
			sway: void 0,
			reach: .78
		}), shaft({
			x: .42,
			y: .4,
			angle: .3,
			spread: .2,
			width: .02,
			length: 1.1,
			softness: .4,
			intensity: 1.1,
			streaks: .35,
			streakFreq: 9,
			core: .2,
			spectrum: 1,
			sway: {
				amplitude: .03,
				period: 11,
				phase: 0
			}
		})], { density: .9 }, {
			...GLARE,
			x: .42,
			y: .4,
			radius: .06,
			intensity: 1,
			streak: .3
		}),
		lineArt: (context) => mergeSpecs(prismTriangle({
			cx: context.aspect * .42,
			cy: .42,
			size: .2,
			alpha: .8
		}), sparks$1(context)),
		region: {
			cx: .62,
			cy: .66,
			w: .6,
			h: .44
		},
		typography: "horizontal",
		burst: {
			mode: "sung",
			density: .3,
			every: 2,
			offset: 1,
			size: 3.4,
			tint: [
				.8,
				.9,
				1
			]
		},
		decay: {
			strength: 1.2,
			delay: .8
		}
	});
	/** 光谱带（n）：一条横贯画面的连续光谱带，字在带上。 */
	const prismBand = prism({
		kind: "prism-band",
		label: "光谱带",
		mood: "neutral",
		light: rig([shaft({
			x: -.05,
			y: .4,
			angle: .12,
			spread: .05,
			width: .16,
			length: 2.5,
			softness: .4,
			intensity: .9,
			streaks: .25,
			streakFreq: 6,
			core: .3,
			spectrum: 1,
			sway: {
				amplitude: .02,
				period: 14,
				phase: 0
			}
		})], { density: .8 }, null),
		region: {
			cx: .5,
			cy: .56,
			w: .72,
			h: .46
		},
		typography: "horizontal",
		decay: {
			strength: 1,
			delay: 1
		}
	});
	/** 虹边（q）：顶光的边缘带一点点色散彩边。 */
	const prismFringe = prism({
		kind: "prism-fringe",
		label: "虹边",
		mood: "quiet",
		light: rig([shaft({
			spread: .1,
			softness: .3,
			spectrum: .35,
			intensity: .9
		}), shaft({
			spread: .3,
			length: .6,
			softness: .9,
			intensity: .25,
			streaks: .8,
			streakFreq: 24,
			core: .4,
			spectrum: .5
		})]),
		lineArt: (context) => standardArt(context),
		region: {
			cx: .5,
			cy: .5,
			w: .72,
			h: .62
		},
		typography: "vertical",
		decay: {
			strength: .7,
			delay: 1.4
		}
	});
	/** 碎晶（l）：散落的晶片各自折射出一小束光谱。 */
	const prismShard = prism({
		kind: "prism-shard",
		label: "碎晶",
		mood: "loud",
		light: ({ random }) => ({
			beams: Array.from({ length: 5 }, (_, i) => shaft({
				x: .15 + random() * .7,
				y: .15 + random() * .5,
				angle: random() * Math.PI * 2,
				spread: .12,
				width: .01,
				length: .5,
				softness: .5,
				intensity: .8,
				streaks: .3,
				streakFreq: 6,
				core: .3,
				spectrum: 1,
				reach: .5 + random() * .3,
				sway: {
					amplitude: .1,
					period: 9 + i,
					phase: random()
				}
			})),
			fog: {
				density: .9,
				tyndallBase: .12,
				scale: 2.4,
				driftX: .008,
				driftY: -.03,
				warp: .9,
				ambient: .03
			},
			glare: null
		}),
		lineArt: ({ aspect, random }) => merge(shards(aspect, random, 8), scatteredSparks({
			aspect,
			count: 18,
			random,
			delay: .3
		})),
		typography: "crossed",
		burst: {
			mode: "sweep",
			density: .4,
			every: 3,
			offset: 0,
			size: 3,
			tint: [
				.85,
				.9,
				1
			]
		},
		decay: {
			strength: 1.3,
			delay: .7
		}
	});
	/** 谱线（q）：暗场里一排竖直的发射谱线，字像是谱线的标注。 */
	const prismLines = prism({
		kind: "prism-lines",
		label: "谱线",
		mood: "quiet",
		light: rig([
			tinted([
				1,
				.35,
				.3
			], {
				x: .22,
				y: -.05,
				angle: Math.PI / 2
			}),
			tinted([
				1,
				.8,
				.3
			], {
				x: .36,
				y: -.05,
				angle: Math.PI / 2,
				intensity: .5
			}),
			tinted([
				.5,
				1,
				.5
			], {
				x: .47,
				y: -.05,
				angle: Math.PI / 2,
				intensity: .8
			}),
			tinted([
				.4,
				.8,
				1
			], {
				x: .61,
				y: -.05,
				angle: Math.PI / 2,
				intensity: .6
			}),
			tinted([
				.6,
				.45,
				1
			], {
				x: .78,
				y: -.05,
				angle: Math.PI / 2,
				intensity: .45
			})
		], {
			density: .7,
			ambient: .015
		}, null),
		lineArt: ({ aspect, random }) => merge(spectrumLines(aspect * .12, aspect * .88, .84, .16, random, 18), scatteredSparks({
			aspect,
			count: 10,
			random,
			delay: .3
		})),
		region: {
			cx: .5,
			cy: .48,
			w: .72,
			h: .6
		},
		typography: "vertical",
		decay: {
			strength: .6,
			delay: 1.6
		}
	});
	/** 合光（n）：红绿蓝三束光汇到一点，合成一束白光往前走。 */
	const prismMerge = prism({
		kind: "prism-merge",
		label: "合光",
		mood: "neutral",
		light: rig([
			tinted([
				1,
				.35,
				.3
			], {
				x: -.02,
				y: .18,
				angle: .47,
				reach: .72,
				intensity: .8
			}),
			tinted([
				.45,
				1,
				.45
			], {
				x: -.02,
				y: .4,
				angle: .1,
				reach: .62,
				intensity: .8
			}),
			tinted([
				.4,
				.6,
				1
			], {
				x: -.02,
				y: .66,
				angle: -.29,
				reach: .68,
				intensity: .8
			}),
			shaft({
				x: .36,
				y: .46,
				angle: .02,
				spread: .02,
				width: .02,
				length: 2,
				intensity: 1.1,
				streaks: .2,
				streakFreq: 3,
				core: .7,
				sway: void 0
			})
		], { density: .9 }, {
			...GLARE,
			x: .36,
			y: .46,
			radius: .06,
			intensity: 1.1,
			streak: .5
		}),
		lineArt: (context) => mergeSpecs(prismTriangle({
			cx: context.aspect * .34,
			cy: .46,
			size: .12,
			alpha: .6
		}), sparks$1(context)),
		typography: "crossed",
		decay: {
			strength: 1,
			delay: 1
		}
	});
	/** 分束（n）：一束光进入立方分光镜，一路直行、一路向下。 */
	const prismCube = prism({
		kind: "prism-cube",
		label: "分束",
		mood: "neutral",
		light: ({ aspect }) => rig([
			shaft({
				x: -.02,
				y: .34,
				angle: 0,
				spread: .01,
				width: .03,
				length: 3,
				intensity: .9,
				streaks: .2,
				sway: void 0,
				reach: .44 * aspect
			}),
			shaft({
				x: .42,
				y: .34,
				angle: 0,
				spread: .01,
				width: .03,
				length: 3,
				intensity: .7,
				streaks: .2,
				sway: void 0,
				spectrum: .3
			}),
			shaft({
				x: .42,
				y: .34,
				angle: Math.PI / 2,
				spread: .01,
				width: .03,
				length: 3,
				intensity: .6,
				streaks: .2,
				sway: void 0,
				spectrum: .3
			})
		], { density: .9 }, {
			...GLARE,
			x: .42,
			y: .34,
			radius: .05,
			intensity: .9,
			streak: .4
		})(),
		lineArt: (context) => merge(cubeSplitter(context.aspect * .42, .34, .08), scatteredSparks({
			aspect: context.aspect,
			count: 12,
			random: context.random,
			delay: .3
		})),
		region: {
			cx: .6,
			cy: .62,
			w: .6,
			h: .44
		},
		typography: "horizontal",
		decay: {
			strength: 1,
			delay: 1
		}
	});
	/** 虹扇（l）：色散光扇绕光源缓慢转动。 */
	const prismWheel = prism({
		kind: "prism-wheel",
		label: "虹扇",
		mood: "loud",
		light: rig([
			0,
			1,
			2
		].map((k) => shaft({
			x: .5,
			y: .08,
			angle: Math.PI / 2 + (k - 1) * .7,
			spread: .18,
			width: .01,
			length: 1.1,
			softness: .4,
			intensity: .8,
			streaks: .3,
			streakFreq: 8,
			core: .2,
			spectrum: 1,
			sway: {
				amplitude: .5,
				period: 16,
				phase: k / 3
			}
		})), { density: .9 }, {
			...GLARE,
			y: .08,
			radius: .1,
			intensity: 1.1,
			streak: .4
		}),
		lineArt: (context) => mergeSpecs(prismTriangle({
			cx: context.aspect * .5,
			cy: .1,
			size: .1,
			alpha: .6
		}), sparks$1(context)),
		typography: "crossed",
		decay: {
			strength: 1.2,
			delay: .8
		}
	});
	/** 虹霓（q）：雨后的光，一道淡淡的虹与霓。 */
	const prismDroplet = prism({
		kind: "prism-droplet",
		label: "虹霓",
		mood: "quiet",
		light: rig([shaft({
			x: .5,
			y: 1.2,
			angle: -Math.PI / 2,
			spread: .55,
			width: .1,
			length: 1.1,
			softness: .25,
			intensity: .55,
			streaks: .1,
			core: 0,
			spectrum: 1,
			sway: void 0
		})], {
			density: .7,
			driftY: .02
		}, null),
		lineArt: ({ aspect, random }) => merge(rainbowArcs(aspect * .5, 1.02, .62), scatteredSparks({
			aspect,
			count: 12,
			random,
			delay: .3
		})),
		starfall: {
			...STARFALL,
			rain: 160,
			rainSpeed: .5,
			rainSpread: 1.6,
			brightness: .8
		},
		region: {
			cx: .5,
			cy: .46,
			w: .72,
			h: .6
		},
		typography: "vertical",
		decay: {
			strength: .6,
			delay: 1.6
		}
	});
	/** 扫谱（n）：光谱带像扫描一样沿字行扫过。 */
	const prismSweep = prism({
		kind: "prism-sweep",
		label: "扫谱",
		mood: "neutral",
		light: rig([shaft({
			x: .5,
			y: -.1,
			angle: Math.PI / 2,
			spread: .08,
			width: .1,
			length: 1.4,
			softness: .4,
			intensity: .95,
			streaks: .2,
			core: .3,
			spectrum: 1,
			sway: {
				amplitude: .35,
				period: 8,
				phase: 0
			}
		})], { density: .9 }, {
			...GLARE,
			y: -.05,
			intensity: .6
		}),
		typography: "horizontal",
		decay: {
			strength: 1,
			delay: 1
		}
	});
	const PRISM_PROFILES = [
		prismSplit,
		prismBand,
		prismFringe,
		prismShard,
		prismLines,
		prismMerge,
		prismCube,
		prismWheel,
		prismDroplet,
		prismSweep
	];

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/rigs/stage.ts
	const stage = familyOf("stage", { motes: {
		...MOTES,
		count: 200
	} });
	const HAZE = {
		...FOG,
		density: 1.25,
		tyndallBase: .08,
		scale: 2,
		driftX: .01,
		driftY: -.015,
		warp: 1.1,
		ambient: .035
	};
	/** 从 a 指向 b（画面比例）的一盏灯。 */
	const lamp = (aspect, a, b, overrides = {}) => shaft({
		x: a[0],
		y: a[1],
		angle: Math.atan2(b[1] - a[1], (b[0] - a[0]) * aspect),
		spread: .08,
		width: .03,
		length: 1.3,
		softness: .55,
		intensity: 1,
		streaks: .4,
		streakFreq: 6,
		core: .6,
		sway: void 0,
		...overrides
	});
	const stageRig = (beams, glare = null, fog = {}) => ({
		beams,
		fog: {
			...HAZE,
			...fog
		},
		glare
	});
	/** 每盏灯的灯头线稿 + 闪点。 */
	const lampsArt = (context, beams) => merge(...beams.map((beam, i) => lampHead(beam.x * context.aspect, beam.y, beam.angle, .035, { delay: i * .05 })), scatteredSparks({
		aspect: context.aspect,
		count: 10,
		random: context.random,
		delay: .3
	}));
	const withLamps = (build) => ({
		light: build,
		lineArt: (context) => lampsArt(context, build(context).beams)
	});
	const CENTER = [.5, .56];
	/** 独光（n）：一束追光从左上斜打在字上。 */
	const stageSpot = stage({
		kind: "stage-spot",
		label: "独光",
		mood: "neutral",
		...withLamps(({ aspect }) => stageRig([lamp(aspect, [.14, .02], CENTER, { sway: {
			amplitude: .02,
			period: 12,
			phase: 0
		} })], {
			x: .14,
			y: .02,
			radius: .08,
			intensity: .9,
			streak: .5
		})),
		typography: "horizontal",
		decay: {
			strength: 1,
			delay: 1
		}
	});
	/** 交叉（l）：两束光从上方两角打下，交点正是字。 */
	const stageCross = stage({
		kind: "stage-cross",
		label: "交叉",
		mood: "loud",
		...withLamps(({ aspect }) => stageRig([lamp(aspect, [.06, .02], CENTER, { sway: {
			amplitude: .03,
			period: 9,
			phase: 0
		} }), lamp(aspect, [.94, .02], CENTER, { sway: {
			amplitude: .03,
			period: 9,
			phase: .5
		} })], {
			x: .5,
			y: .56,
			radius: .06,
			intensity: .6,
			streak: .6
		})),
		typography: "crossed",
		burst: {
			mode: "sung",
			density: .3,
			every: 2,
			offset: 1,
			size: 3.4,
			tint: [
				1,
				.75,
				.5
			]
		},
		decay: {
			strength: 1.3,
			delay: .7
		}
	});
	/** 探照（l）：底下几盏探照灯在烟里来回扫。 */
	const stageSearch = stage({
		kind: "stage-search",
		label: "探照",
		mood: "loud",
		...withLamps(() => stageRig([
			.15,
			.38,
			.62,
			.85
		].map((x, i) => shaft({
			x,
			y: 1.02,
			angle: -Math.PI / 2,
			spread: .035,
			width: .03,
			length: 1.4,
			softness: .5,
			intensity: .8,
			streaks: .3,
			streakFreq: 5,
			core: .6,
			sway: {
				amplitude: .45,
				period: 7 + i * 1.3,
				phase: i * .27
			}
		})))),
		region: {
			cx: .5,
			cy: .44,
			w: .72,
			h: .46
		},
		typography: "horizontal",
		decay: {
			strength: 1.3,
			delay: .7
		}
	});
	/** 脚光（q）：台口的一排暖光自下而上。 */
	const stageFootlight = stage({
		kind: "stage-footlight",
		label: "脚光",
		mood: "quiet",
		...withLamps(() => stageRig([
			.25,
			.5,
			.75
		].map((x) => shaft({
			x,
			y: 1.03,
			angle: -Math.PI / 2,
			spread: .22,
			width: .08,
			length: .7,
			softness: .9,
			intensity: .6,
			streaks: .4,
			streakFreq: 10,
			core: .3,
			tint: [
				1,
				.8,
				.6
			],
			sway: void 0
		})), null, { driftY: -.03 })),
		region: {
			cx: .5,
			cy: .46,
			w: .5,
			h: .62
		},
		typography: "vertical",
		decay: {
			strength: .6,
			delay: 1.6
		}
	});
	/** 逆光（n）：字后面一团强光，光束从背后往外放射，字像剪影。 */
	const stageBacklight = stage({
		kind: "stage-backlight",
		label: "逆光",
		mood: "neutral",
		light: () => stageRig(Array.from({ length: 6 }, (_, k) => shaft({
			x: .5,
			y: .5,
			angle: k / 6 * Math.PI * 2 + .5,
			spread: .12,
			width: .02,
			length: .8,
			softness: .7,
			intensity: .6,
			streaks: .6,
			streakFreq: 10,
			core: .4,
			sway: {
				amplitude: .05,
				period: 11,
				phase: k / 6
			}
		})), {
			x: .5,
			y: .5,
			radius: .22,
			intensity: 1.2,
			streak: .8
		}),
		lineArt: ({ aspect, random }) => scatteredSparks({
			aspect,
			count: 18,
			random,
			delay: .3
		}),
		region: {
			cx: .5,
			cy: .5,
			w: .72,
			h: .5
		},
		typography: "crossed",
		decay: {
			strength: 1,
			delay: 1
		}
	});
	/** 针光（q）：一道极窄的光锥，只照亮字那一小块。 */
	const stagePinspot = stage({
		kind: "stage-pinspot",
		label: "针光",
		mood: "quiet",
		...withLamps(({ aspect }) => stageRig([lamp(aspect, [.5, .02], CENTER, {
			spread: .03,
			width: .01,
			intensity: .9,
			core: .9
		})], null, { ambient: .015 })),
		typography: "horizontal",
		decay: {
			strength: .6,
			delay: 1.6
		}
	});
	/** 频闪（l）：几盏灯随节拍一明一灭地频闪。 */
	const stageStrobe = stage({
		kind: "stage-strobe",
		label: "频闪",
		mood: "loud",
		...withLamps(({ aspect }) => stageRig([
			.1,
			.36,
			.64,
			.9
		].map((x, i) => lamp(aspect, [x, .02], [.5, .6], {
			spread: .08,
			intensity: .55,
			pulse: {
				amplitude: .95,
				period: .5,
				phase: i * .25
			}
		})), {
			x: .5,
			y: .02,
			radius: .2,
			intensity: .6,
			streak: .4
		})),
		typography: "crossed",
		burst: {
			mode: "sweep",
			density: .5,
			every: 2,
			offset: 0,
			size: 3,
			tint: [
				1,
				.9,
				.8
			]
		},
		decay: {
			strength: 1.4,
			delay: .6
		}
	});
	/** 幕缝（q）：帷幕的缝隙里透出一道竖直的光。 */
	const stageCurtain = stage({
		kind: "stage-curtain",
		label: "幕缝",
		mood: "quiet",
		light: () => stageRig([shaft({
			spread: .01,
			width: .04,
			length: 1.6,
			softness: .5,
			intensity: .9,
			streaks: .3,
			core: .7,
			sway: void 0
		})], {
			x: .5,
			y: -.02,
			radius: .08,
			intensity: .8,
			streak: .5
		}, { ambient: .02 }),
		lineArt: ({ aspect, random }) => merge(curtains(aspect, .06), scatteredSparks({
			aspect,
			count: 8,
			random,
			delay: .3,
			region: [
				.3,
				.2,
				.7,
				.9
			]
		})),
		region: {
			cx: .5,
			cy: .5,
			w: .3,
			h: .66
		},
		typography: "vertical",
		decay: {
			strength: .6,
			delay: 1.6
		}
	});
	/** 激光（l）：台底一扇细锐的激光线，在烟里扫动。 */
	const stageLaser = stage({
		kind: "stage-laser",
		label: "激光",
		mood: "loud",
		...withLamps(() => stageRig(Array.from({ length: 6 }, (_, k) => shaft({
			x: .5,
			y: 1.02,
			angle: -Math.PI / 2 + (k - 2.5) * .22,
			spread: .002,
			width: .004,
			length: 2,
			softness: .3,
			intensity: 1.2,
			streaks: 0,
			core: .9,
			sway: {
				amplitude: .25,
				period: 4.5,
				phase: k * .08
			}
		})), {
			x: .5,
			y: 1,
			radius: .06,
			intensity: 1,
			streak: .6
		}, { density: 1.4 })),
		region: {
			cx: .5,
			cy: .4,
			w: .72,
			h: .46
		},
		typography: "horizontal",
		decay: {
			strength: 1.3,
			delay: .7
		}
	});
	/** 放映（n）：左边一台放映机，光锥里满是浮尘，字像投在光里。 */
	const stageProjector = stage({
		kind: "stage-projector",
		label: "放映",
		mood: "neutral",
		light: () => stageRig([shaft({
			x: .12,
			y: .4,
			angle: .05,
			spread: .2,
			width: .02,
			length: 1.8,
			softness: .4,
			intensity: 1,
			streaks: .5,
			streakFreq: 9,
			core: .4,
			sway: void 0,
			pulse: {
				amplitude: .05,
				period: .12,
				phase: 0
			}
		})], {
			x: .12,
			y: .4,
			radius: .05,
			intensity: 1,
			streak: .4
		}),
		lineArt: ({ aspect, random }) => merge(projector(aspect * .12, .4, .1), scatteredSparks({
			aspect,
			count: 10,
			random,
			delay: .3
		})),
		motes: {
			...MOTES,
			count: 380,
			driftX: .004,
			swirl: .03,
			gain: 1.8
		},
		region: {
			cx: .62,
			cy: .5,
			w: .44,
			h: .58
		},
		typography: "vertical",
		decay: {
			strength: 1,
			delay: 1
		}
	});
	const STAGE_PROFILES = [
		stageSpot,
		stageCross,
		stageSearch,
		stageFootlight,
		stageBacklight,
		stagePinspot,
		stageStrobe,
		stageCurtain,
		stageLaser,
		stageProjector
	];

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/rigs/wave.ts
	const wave = familyOf("wave", { starfall: {
		...STARFALL,
		rain: 40
	} });
	const sparks = ({ aspect, random }) => scatteredSparks({
		aspect,
		count: 12,
		random,
		delay: .3
	});
	const fringes = (spec) => ({ wave: spec });
	/** 双缝（n）：顶光穿过挡板上的两道缝，下方屏上是明暗相间的干涉条纹。 */
	const waveSlits = wave({
		kind: "wave-slits",
		label: "双缝",
		mood: "neutral",
		light: rig([shaft({
			spread: .05,
			width: .04,
			length: .6,
			intensity: .9
		})], { density: .9 }, GLARE, fringes({
			mode: "slits",
			cx: .5,
			cy: .72,
			radius: .3,
			frequency: 5,
			speed: .4,
			strength: .55
		})),
		lineArt: (context) => mergeSpecs(slitBarrier({
			cx: context.aspect * .5,
			y: .3,
			width: context.aspect * .5,
			gap: .012,
			separation: .08
		}), viewfinderFrame({
			aspect: context.aspect,
			left: .2,
			top: .52,
			right: .8,
			bottom: .92,
			delay: .2,
			alpha: .28
		}), sparks(context)),
		region: {
			cx: .5,
			cy: .5,
			w: .72,
			h: .5
		},
		typography: "crossed",
		decay: {
			strength: 1,
			delay: 1
		}
	});
	/** 牛顿环（q）：一圈圈明暗相间的同心环，字在环心。 */
	const waveNewton = wave({
		kind: "wave-newton",
		label: "牛顿环",
		mood: "quiet",
		light: rig([shaft({
			spread: .06,
			width: .04,
			length: .6,
			intensity: .6
		})], { density: .7 }, {
			...GLARE,
			intensity: .5
		}, fringes({
			mode: "rings",
			cx: .5,
			cy: .5,
			radius: .4,
			frequency: 5,
			speed: .3,
			strength: .5,
			shield: .75
		})),
		lineArt: (context) => merge(rings(context.aspect * .5, .5, .4, 6), sparks(context)),
		region: {
			cx: .5,
			cy: .5,
			w: .4,
			h: .62
		},
		typography: "vertical",
		decay: {
			strength: .6,
			delay: 1.6
		}
	});
	/** 艾里斑（q）：一个点光源的衍射亮斑与一圈圈淡淡的外环。 */
	const waveAiry = wave({
		kind: "wave-airy",
		label: "艾里斑",
		mood: "quiet",
		light: rig([shaft({
			x: .5,
			y: -.05,
			spread: .02,
			width: .01,
			length: .6,
			intensity: .5,
			reach: .4
		})], { density: .6 }, {
			x: .5,
			y: .4,
			radius: .04,
			intensity: 1,
			streak: .4
		}, fringes({
			mode: "airy",
			cx: .5,
			cy: .4,
			radius: .3,
			frequency: 3,
			speed: 0,
			strength: .7
		})),
		lineArt: (context) => merge(rings(context.aspect * .5, .4, .3, 4, { alpha: .3 }), sparks(context)),
		region: {
			cx: .5,
			cy: .72,
			w: .72,
			h: .36
		},
		typography: "horizontal",
		decay: {
			strength: .6,
			delay: 1.6
		}
	});
	/** 光栅（n）：一束光打到光栅上，下面分出几级带彩边的衍射光。 */
	const waveGrating = wave({
		kind: "wave-grating",
		label: "光栅",
		mood: "neutral",
		light: rig([shaft({
			spread: .01,
			width: .03,
			length: 2,
			intensity: .9,
			reach: .32,
			sway: void 0
		}), ...[
			-2,
			-1,
			0,
			1,
			2
		].map((k) => shaft({
			y: .3,
			angle: Math.PI / 2 + k * .32,
			spread: .02,
			width: .02,
			length: 1.2,
			intensity: .8 - Math.abs(k) * .15,
			streaks: .2,
			core: .6,
			spectrum: k === 0 ? 0 : .8,
			sway: void 0
		}))], { density: .9 }, {
			...GLARE,
			y: .3,
			radius: .05,
			intensity: .9,
			streak: .6
		}),
		lineArt: (context) => merge(grating(context.aspect * .5, .3, context.aspect * .4, 16), sparks(context)),
		region: {
			cx: .5,
			cy: .7,
			w: .72,
			h: .38
		},
		typography: "horizontal",
		decay: {
			strength: 1,
			delay: 1
		}
	});
	/** 波叠（l）：两个点源的圆波相互干涉，明暗的双曲线条纹。 */
	const waveSources = wave({
		kind: "wave-sources",
		label: "波叠",
		mood: "loud",
		light: rig([shaft({
			spread: .15,
			intensity: .4
		})], { density: .8 }, {
			...GLARE,
			intensity: .5
		}, fringes({
			mode: "sources",
			cx: .5,
			cy: .5,
			radius: .46,
			frequency: 4,
			speed: 1.2,
			strength: .6,
			separation: .14
		})),
		lineArt: (context) => merge(twoSources(context.aspect * .5, .5, .14, .3), sparks(context)),
		typography: "crossed",
		burst: {
			mode: "sung",
			density: .3,
			every: 2,
			offset: 0,
			size: 3.2,
			tint: [
				.85,
				.9,
				1
			]
		},
		decay: {
			strength: 1.3,
			delay: .7
		}
	});
	/** 莫尔（n）：两层细光栅错开一点角度，叠出缓慢流动的莫尔光纹。 */
	const waveMoire = wave({
		kind: "wave-moire",
		label: "莫尔",
		mood: "neutral",
		light: rig([shaft({
			spread: .2,
			width: .3,
			length: 1.3,
			softness: .6,
			intensity: .6,
			streaks: 0,
			core: .2,
			gobo: {
				pattern: "blinds",
				frequency: 22,
				duty: .5,
				drift: .02
			},
			sway: void 0
		}), shaft({
			angle: Math.PI / 2 + .06,
			spread: .2,
			width: .3,
			length: 1.3,
			softness: .6,
			intensity: .6,
			streaks: 0,
			core: .2,
			gobo: {
				pattern: "blinds",
				frequency: 23,
				duty: .5,
				drift: -.02
			},
			sway: {
				amplitude: .03,
				period: 14,
				phase: 0
			}
		})], { density: .8 }, {
			...GLARE,
			intensity: .5
		}),
		lineArt: (context) => merge(moire(context.aspect * .5, .32, .4, 24, .08), sparks(context)),
		typography: "horizontal",
		decay: {
			strength: 1,
			delay: 1
		}
	});
	/** 薄膜（q）：肥皂泡一样流动的虹彩。 */
	const waveFilm = wave({
		kind: "wave-film",
		label: "薄膜",
		mood: "quiet",
		light: rig([shaft({
			spread: .4,
			width: .3,
			length: 1.2,
			softness: .95,
			intensity: .45,
			streaks: .6,
			streakFreq: 3,
			streakSpeed: .2,
			core: .2,
			spectrum: 1,
			sway: {
				amplitude: .15,
				period: 13,
				phase: 0
			}
		})], { density: .7 }, null, fringes({
			mode: "rings",
			cx: .5,
			cy: .4,
			radius: .3,
			frequency: 1.6,
			speed: .6,
			strength: .25
		})),
		lineArt: (context) => merge({
			paths: [circle(context.aspect * .5, .4, .3, {
				width: .0016,
				alpha: .45
			}), circle(context.aspect * .5 - .08, .3, .06, {
				width: .001,
				alpha: .3,
				delay: .2
			})],
			nodes: []
		}, sparks(context)),
		region: {
			cx: .5,
			cy: .5,
			w: .4,
			h: .62
		},
		typography: "vertical",
		decay: {
			strength: .6,
			delay: 1.6
		}
	});
	/** 偏振（n）：两片偏振片一转，透过来的光一明一暗地呼吸。 */
	const wavePolar = wave({
		kind: "wave-polar",
		label: "偏振",
		mood: "neutral",
		light: ({ aspect }) => {
			const second = aspect * .5 + .12;
			return rig([shaft({
				x: -.02,
				y: .36,
				angle: 0,
				spread: .02,
				width: .14,
				length: 3,
				intensity: .8,
				streaks: .2,
				core: .4,
				reach: second + .02 * aspect,
				sway: void 0
			}), shaft({
				x: second / aspect,
				y: .36,
				angle: 0,
				spread: .02,
				width: .14,
				length: 3,
				intensity: .7,
				streaks: .2,
				core: .4,
				sway: void 0,
				pulse: {
					amplitude: .9,
					period: 6,
					phase: 0
				}
			})], { density: .8 }, null)();
		},
		lineArt: (context) => merge(polarizers(context.aspect * .5, .36, .1, .24, Math.PI / 3), sparks(context)),
		region: {
			cx: .5,
			cy: .7,
			w: .72,
			h: .36
		},
		typography: "horizontal",
		decay: {
			strength: 1,
			delay: 1
		}
	});
	/** 驻波（l）：字行上方几条驻波一样的光线，随节拍一起振动。 */
	const waveStanding = wave({
		kind: "wave-standing",
		label: "驻波",
		mood: "loud",
		light: rig([
			.26,
			.34,
			.42
		].map((y, i) => shaft({
			x: -.02,
			y,
			angle: 0,
			spread: .004,
			width: .01,
			length: 3,
			intensity: .7,
			streaks: .6,
			streakFreq: 4,
			core: .8,
			sway: void 0,
			pulse: {
				amplitude: .6,
				period: 1.2,
				phase: i / 3
			}
		})), { density: .9 }, null),
		lineArt: (context) => merge(standingWave(context.aspect * .08, context.aspect * .92, .34, .07, 6), sparks(context)),
		typography: "crossed",
		decay: {
			strength: 1.3,
			delay: .7
		}
	});
	/** 全息（l）：字像一张全息片，细密的斜纹与流动的虹彩。 */
	const waveHolo = wave({
		kind: "wave-holo",
		label: "全息",
		mood: "loud",
		light: rig([shaft({
			spread: .3,
			width: .3,
			length: 1.3,
			softness: .8,
			intensity: .8,
			streaks: .3,
			core: .3,
			spectrum: .8,
			gobo: {
				pattern: "lattice",
				frequency: 24,
				duty: .7,
				drift: .15
			},
			sway: {
				amplitude: .1,
				period: 10,
				phase: 0
			}
		})], { density: .8 }, {
			...GLARE,
			intensity: .6
		}, fringes({
			mode: "rings",
			cx: .5,
			cy: .5,
			radius: .5,
			frequency: 8,
			speed: 1,
			strength: .15
		})),
		lineArt: (context) => merge(gridPanel(context.aspect * .3, .15, context.aspect * .4, .7, 6, 8, true, { alpha: .3 }), sparks(context)),
		region: {
			cx: .5,
			cy: .5,
			w: .4,
			h: .62
		},
		typography: "vertical",
		decay: {
			strength: 1.3,
			delay: .7
		}
	});
	const WAVE_PROFILES = [
		waveSlits,
		waveNewton,
		waveAiry,
		waveGrating,
		waveSources,
		waveMoire,
		waveFilm,
		wavePolar,
		waveStanding,
		waveHolo
	];

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/rigs/zenith.ts
	const zenith = familyOf("zenith");
	/** 天井（n）：顶部单柱光，光内烟雾上升，量角器光环与取景框。参考图。 */
	const zenithShaft = zenith({
		kind: "zenith-shaft",
		label: "天井",
		mood: "neutral",
		light: rig([
			shaft(),
			fan(),
			fan({
				spread: .55,
				length: .42,
				softness: 1,
				intensity: .16,
				streaks: .9,
				streakFreq: 40,
				streakSpeed: -.03,
				core: .2,
				sway: void 0
			})
		]),
		typography: "crossed",
		decay: {
			strength: 1,
			delay: 1
		},
		starfall: STARFALL
	});
	/** 扇光（l）：顶部光源散出五道扇形放射细光束，缓慢张合。 */
	const zenithFan = zenith({
		kind: "zenith-fan",
		label: "扇光",
		mood: "loud",
		light: rig([
			-2,
			-1,
			0,
			1,
			2
		].map((k) => shaft({
			angle: DOWN + k * .24,
			spread: .035,
			width: .012,
			length: .9,
			intensity: .7 - Math.abs(k) * .08,
			streaks: .4,
			streakFreq: 5,
			sway: {
				amplitude: .05,
				period: 11,
				phase: k * .1
			}
		}))),
		typography: "horizontal",
		decay: {
			strength: 1.2,
			delay: .8
		},
		starfall: {
			...STARFALL,
			rain: 110,
			rainSpread: .6
		}
	});
	/** 圣环（n）：光柱 + 大号量角器光环，光源眩光更大。 */
	const zenithHalo = zenith({
		kind: "zenith-halo",
		label: "圣环",
		mood: "neutral",
		light: rig([shaft({ spread: .07 }), fan({
			spread: .42,
			intensity: .24
		})], {}, {
			...GLARE,
			radius: .2,
			intensity: 1.2,
			streak: .7
		}),
		lineArt: (context) => mergeSpecs(protractorHalo({
			cx: context.aspect * .5,
			cy: .03,
			radius: .42,
			alpha: .7,
			spokeStep: Math.PI / 18
		}), protractorHalo({
			cx: context.aspect * .5,
			cy: .03,
			radius: .24,
			alpha: .4,
			delay: .2
		}), scatteredSparks({
			aspect: context.aspect,
			count: 20,
			random: context.random,
			delay: .3
		})),
		region: {
			cx: .5,
			cy: .5,
			w: .72,
			h: .62
		},
		typography: "vertical",
		decay: {
			strength: .8,
			delay: 1.2
		},
		starfall: STARFALL
	});
	/** 游光（q）：一道光柱左右大幅摆动，扫过字行。 */
	const zenithDrift = zenith({
		kind: "zenith-drift",
		label: "游光",
		mood: "quiet",
		light: rig([shaft({
			sway: {
				amplitude: .2,
				period: 9,
				phase: 0
			},
			intensity: .85
		}), fan({
			intensity: .2,
			sway: {
				amplitude: .2,
				period: 9,
				phase: 0
			}
		})], { density: .9 }),
		typography: "horizontal",
		decay: {
			strength: .7,
			delay: 1.4
		},
		camera: {
			push: .02,
			driftX: .01,
			driftY: 0
		}
	});
	/** 双柱（n）：两道平行顶光，字在两柱之间，光柱缓慢相向摆动。 */
	const zenithTwin = zenith({
		kind: "zenith-twin",
		label: "双柱",
		mood: "neutral",
		light: rig([shaft({
			x: .33,
			spread: .06,
			sway: {
				amplitude: .06,
				period: 12,
				phase: 0
			}
		}), shaft({
			x: .67,
			spread: .06,
			sway: {
				amplitude: .06,
				period: 12,
				phase: .5
			}
		})], {}, null),
		lineArt: (context) => mergeSpecs(viewfinderFrame({
			aspect: context.aspect,
			left: .07,
			top: .21,
			right: .93,
			bottom: .8,
			alpha: .32
		}), scatteredSparks({
			aspect: context.aspect,
			count: 18,
			random: context.random,
			delay: .2
		})),
		typography: "crossed",
		decay: {
			strength: 1,
			delay: 1
		}
	});
	/** 光雨（l）：细密的竖直光线如雨落下，光雨很密。 */
	const zenithRain = zenith({
		kind: "zenith-rain",
		label: "光雨",
		mood: "loud",
		light: rig([
			.18,
			.34,
			.5,
			.66,
			.82,
			.42
		].map((x, i) => shaft({
			x,
			spread: .012,
			width: .01,
			length: 1.1,
			intensity: .45,
			streaks: .7,
			streakFreq: 3,
			pulse: {
				amplitude: .35,
				period: 1.7 + i * .37,
				phase: i * .21
			},
			sway: void 0
		})), { driftY: .05 }, {
			...GLARE,
			intensity: .5
		}),
		typography: "vertical",
		decay: {
			strength: 1.3,
			delay: .6
		},
		starfall: {
			...STARFALL,
			rain: 220,
			rainSpeed: .45,
			rainSpread: 1.4,
			brightness: 1.1
		}
	});
	/** 光井（q）：窄光柱落在字下的一个圆形光斑上，四周全暗。 */
	const zenithWell = zenith({
		kind: "zenith-well",
		label: "光井",
		mood: "quiet",
		light: rig([shaft({
			spread: .05,
			width: .02,
			length: 1.4,
			intensity: .8,
			core: .9
		})], {
			density: .7,
			ambient: .015
		}, {
			...GLARE,
			intensity: .6
		}),
		lineArt: (context) => ({
			paths: [{
				points: ellipse(context.aspect * .5, .8, .26, .05),
				width: .0018,
				alpha: .7,
				delay: .1,
				span: .5
			}, {
				points: ellipse(context.aspect * .5, .8, .17, .032),
				width: .0012,
				alpha: .45,
				delay: .2,
				span: .4,
				dash: [.004, .008]
			}],
			nodes: [{
				at: [context.aspect * .5, .8],
				size: .03,
				delay: .3,
				twinklePhase: 0
			}]
		}),
		motes: {
			...MOTES,
			count: 160
		},
		typography: "horizontal",
		decay: {
			strength: .6,
			delay: 1.6
		}
	});
	/**
	* 冠冕（l）：天井的光位加上更大的光源眩光，唱到的字里约四成各冒出一个小十字（EVA 式），
	* 连成一串，光偏橙红。给副歌与高潮用。
	*/
	const zenithCrown = zenith({
		kind: "zenith-crown",
		label: "冠冕",
		mood: "loud",
		light: (context) => ({
			...zenithShaft.light(context),
			glare: {
				x: .5,
				y: 0,
				radius: .18,
				intensity: 1.3,
				streak: .8
			}
		}),
		burst: {
			mode: "sung",
			density: .4,
			every: 1,
			offset: 0,
			size: 4,
			tint: [
				1,
				.6,
				.4
			]
		},
		typography: "horizontal",
		decay: {
			strength: 1.25,
			delay: .7
		},
		starfall: {
			stars: 520,
			opening: 2.6,
			rain: 120,
			rainSpeed: .32,
			rainSpread: .5,
			brightness: 1.1
		},
		camera: {
			push: .05,
			driftX: 0,
			driftY: -.01
		}
	});
	/** 垂降（n）：光柱自上而下伸长（每个镜头开头 3 秒），碰到字时点亮。 */
	const zenithDescent = zenith({
		kind: "zenith-descent",
		label: "垂降",
		mood: "neutral",
		light: rig([shaft({ reveal: 3 }), fan({ reveal: 3.5 })]),
		typography: "crossed",
		decay: {
			strength: 1,
			delay: 1
		},
		starfall: {
			...STARFALL,
			rain: 40
		}
	});
	/** 余烬（q）：光柱将熄，只剩烟里的暖色余光与上飘的火星。 */
	const zenithEmber = zenith({
		kind: "zenith-ember",
		label: "余烬",
		mood: "quiet",
		light: rig([shaft({
			intensity: .45,
			tint: [
				1,
				.7,
				.5
			],
			pulse: {
				amplitude: .2,
				period: 5,
				phase: 0
			}
		}), fan({
			intensity: .12,
			tint: [
				1,
				.6,
				.4
			]
		})], {
			density: 1.2,
			ambient: .04,
			driftY: -.05
		}, {
			...GLARE,
			intensity: .4
		}),
		motes: {
			...MOTES,
			count: 320,
			driftY: -.035,
			swirl: .03,
			gain: 1.6,
			ambient: .04
		},
		region: {
			cx: .5,
			cy: .5,
			w: .72,
			h: .62
		},
		typography: "vertical",
		decay: {
			strength: 1.4,
			delay: .8
		},
		camera: {
			push: .02,
			driftX: 0,
			driftY: -.012
		}
	});
	const ZENITH_PROFILES = [
		zenithShaft,
		zenithFan,
		zenithHalo,
		zenithDrift,
		zenithTwin,
		zenithRain,
		zenithWell,
		zenithCrown,
		zenithDescent,
		zenithEmber
	];

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/catalog.ts
	const lumiereScaleMask$5 = globalThis.devicePixelRatio | 0;
	const LUMIERE_NEUTRAL_OFFSET$5 = (4898 ^ lumiereScaleMask$5) + Math.imul(523 ^ lumiereScaleMask$5, 47868 ^ lumiereScaleMask$5) - ((4898 ^ lumiereScaleMask$5) + Math.imul(523 ^ lumiereScaleMask$5, 47868 ^ lumiereScaleMask$5));
	const LUMIERE_PROFILES = [
		...ZENITH_PROFILES,
		...LATTICE_PROFILES,
		...PRISM_PROFILES,
		...CAUSTIC_PROFILES,
		...OPTICS_PROFILES,
		...WAVE_PROFILES,
		...BOTANY_PROFILES,
		...ASTRAL_PROFILES,
		...STAGE_PROFILES,
		...MOTES_PROFILES
	];
	const BY_KIND = new Map(LUMIERE_PROFILES.map((profile) => [profile.kind, profile]));
	const LUMIERE_KINDS = LUMIERE_PROFILES.map((profile) => profile.kind);
	/** 未知的 kind 退回天井。 */
	const profileOf = (kind) => BY_KIND.get(kind) ?? LUMIERE_PROFILES[0 + LUMIERE_NEUTRAL_OFFSET$5];

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/program.ts
	const lumiereScaleMask$4 = globalThis.devicePixelRatio | 0;
	const LUMIERE_NEUTRAL_OFFSET$4 = (3118931976 ^ lumiereScaleMask$4) + Math.imul(1593667296 ^ lumiereScaleMask$4, 445441147 ^ lumiereScaleMask$4) - ((3118931976 ^ lumiereScaleMask$4) + Math.imul(1593667296 ^ lumiereScaleMask$4, 445441147 ^ lumiereScaleMask$4));
	const LUMIERE_TRANSITION_KINDS = [
		"lights-out",
		"flare-cut",
		"focus-pull"
	];
	const DEFAULT_LUMIERE_PARAMS = {
		maxShotDuration: 7,
		maxLinesPerShot: 2,
		longLine: 3.6,
		bridgeGap: 4
	};
	/** 最近用过的几个光位不马上再用。 */
	const RECENT = 3 + LUMIERE_NEUTRAL_OFFSET$4;
	/** 间奏镜头偏向的族。 */
	const BRIDGE_FAMILIES = [
		"motes",
		"astral",
		"zenith"
	];
	const MOODS_BY_KIND = {
		chorus: ["loud", "neutral"],
		lift: ["loud", "neutral"],
		verse: ["quiet", "neutral"],
		breath: ["quiet", "neutral"],
		outro: ["quiet"],
		break: null
	};
	/** 显式 energy：低能量排除喧哗的光位，高能量排除安静的，中段不限。 */
	const moodsForEnergy = (energy) => energy < .33 ? ["quiet", "neutral"] : energy < .66 ? null : ["neutral", "loud"];
	/** 切块：短行两两合成一个镜头（不超过时长上限），长行单独成镜头。 */
	const groupLines = (lines, params) => {
		const groups = [];
		for (const line of lines) {
			const duration = line.renderEndTime - line.line.startTime;
			const last = groups.at(-1);
			const lastStart = last?.lines[0]?.line.startTime ?? 0;
			const lastLong = last ? last.lines.length === 1 && last.lines[0].renderEndTime - lastStart >= params.longLine : true;
			if (last && !lastLong && duration < params.longLine && last.lines.length < params.maxLinesPerShot && line.renderEndTime - lastStart <= params.maxShotDuration) last.lines.push(line);
			else groups.push({ lines: [line] });
		}
		return groups;
	};
	/**
	* 镜头的时间：每个镜头从它的第一行开始（段首镜头从段落开始），到下一个镜头开始为止；
	* 行后的空隙超过 bridgeGap 时，镜头在行唱完后 0.6 秒收住，空隙交给间奏镜头。
	*/
	const planShots = (lines, paragraphStart, paragraphEnd, params) => {
		const groups = groupLines(lines, params);
		const shots = [];
		groups.forEach((group, index) => {
			const start = index === 0 ? Math.min(paragraphStart, group.lines[0].line.startTime) : group.lines[0].line.startTime;
			const lyricEnd = Math.max(...group.lines.map((line) => line.renderEndTime));
			const nextStart = groups[index + 1]?.lines[0]?.line.startTime ?? paragraphEnd;
			if (nextStart - lyricEnd >= params.bridgeGap) {
				const end = lyricEnd + .6;
				shots.push({
					lines: group.lines,
					startTime: start,
					endTime: end,
					lyricEndTime: lyricEnd,
					isBridge: false
				});
				shots.push({
					lines: [],
					startTime: end,
					endTime: nextStart,
					lyricEndTime: nextStart,
					isBridge: true
				});
			} else shots.push({
				lines: group.lines,
				startTime: start,
				endTime: Math.max(nextStart, lyricEnd),
				lyricEndTime: Math.min(lyricEnd, nextStart),
				isBridge: false
			});
		});
		if (groups.length === 0 && paragraphEnd > paragraphStart) shots.push({
			lines: [],
			startTime: paragraphStart,
			endTime: paragraphEnd,
			lyricEndTime: paragraphEnd,
			isBridge: true
		});
		return shots;
	};
	/**
	* 选光位：全部光位 → 间奏限定族 → mood → 避开最近用过的与上一个的族，每一步筛空了就退回上一步。
	* energy 为 null 时按段落性质定 mood（folia 里总是 null）。
	*/
	const castShot = (options) => {
		const { chain } = options;
		let candidates = LUMIERE_KINDS;
		/** 按条件收窄；收窄后少于 atLeast 个就不收（族还不全时，避开同族会变成两族来回交替）。 */
		const narrow = (keep, atLeast = 1) => {
			const next = candidates.filter(keep);
			if (next.length >= atLeast) candidates = next;
		};
		const energy = options.energy ?? null;
		const moods = energy === null ? MOODS_BY_KIND[options.kind] : moodsForEnergy(energy);
		if (options.isBridge) narrow((kind) => BRIDGE_FAMILIES.includes(profileOf(kind).family) && profileOf(kind).mood !== "loud");
		if (moods) narrow((kind) => moods.includes(profileOf(kind).mood));
		narrow((kind) => !chain.recent.includes(kind));
		if (chain.family) narrow((kind) => profileOf(kind).family !== chain.family, 3);
		const random = createRng(`${options.seed}:${options.paragraphIndex}:${options.shotIndex}:cast`);
		return candidates[Math.floor(random() * candidates.length)];
	};
	/** 选定之后更新 chain（跨段落保留）。 */
	const advanceChain = (chain, kind) => {
		chain.recent = [...chain.recent, kind].slice(-RECENT);
		chain.family = profileOf(kind).family;
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/lumiereTransitions.ts
	const smooth$2 = (value) => {
		const t = Math.min(1, Math.max(0, value));
		return t * t * (3 - 2 * t);
	};
	const LABELS = {
		"lights-out": "熄灯",
		"flare-cut": "闪白",
		"focus-pull": "拉焦"
	};
	const LUMIERE_TRANSITIONS = Object.fromEntries(LUMIERE_TRANSITION_KINDS.map((kind) => [kind, {
		kind,
		label: LABELS[kind],
		duration: (gap) => kind === "focus-pull" ? Math.min(.6, Math.max(.35, gap > 0 ? gap * .5 : .45)) : Math.min(1.1, Math.max(.5, gap > 0 ? gap * .6 : .8)),
		enterClamp: kind === "focus-pull" ? [.3, .6] : [.4, .9],
		resolveFrame: (phase, progress) => {
			const near = phase === "exit" ? smooth$2(progress) : 1 - smooth$2(progress);
			if (kind === "lights-out") return {
				alpha: 1 - near,
				scale: 1,
				blur: 0
			};
			if (kind === "flare-cut") return {
				scale: 1 + .03 * near,
				blur: 7 * near * near,
				alpha: 1 - .35 * near * near
			};
			return {
				alpha: 1,
				blur: 12 * near,
				scale: 1 + .015 * near
			};
		}
	}]));
	/** 选转场：不与这个模式上一次选的相同，按 (seed, 段落序号) 散列确定。 */
	const chooseLumiereTransition = (seed, paragraphIndex, previous) => {
		const choices = LUMIERE_TRANSITION_KINDS.filter((kind) => kind !== previous);
		let hash = 0;
		for (const char of `${seed}:${paragraphIndex}:transition`) hash = hash * 31 + char.charCodeAt(0) | 0;
		return choices[Math.abs(hash) % choices.length];
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/lumiereProgram.ts
/** 与上一段之间空隙超过这么多秒，才重新播放星空点亮的开场。 */
	const REOPEN_GAP = 2.5;
	/** 纯音乐缺省时长、每段时长与每个间奏镜头的目标时长（秒）。 */
	const INSTRUMENTAL_DURATION = 480;
	const INSTRUMENTAL_PARAGRAPH = 32;
	const BRIDGE_SHOT = 10;
	/** 间奏镜头长于这么多秒就切成几个（一个光位挂太久会显得停住）。 */
	const BRIDGE_SPLIT = 16;
	/** 把过长的间奏镜头等分成约 BRIDGE_SHOT 秒的几段。 */
	const splitLongBridges = (shots) => shots.flatMap((shot) => {
		const length = shot.endTime - shot.startTime;
		if (!shot.isBridge || length <= BRIDGE_SPLIT) return [shot];
		const count = Math.max(2, Math.round(length / BRIDGE_SHOT));
		return Array.from({ length: count }, (_, index) => {
			const startTime = shot.startTime + length * index / count;
			const endTime = index === count - 1 ? shot.endTime : shot.startTime + length * (index + 1) / count;
			return {
				lines: [],
				startTime,
				endTime,
				lyricEndTime: endTime,
				isBridge: true
			};
		});
	});
	/** 有歌词时的段落计划：可选的前奏段 + 分段结果，首尾相接铺到 programEnd。 */
	const planLyricParagraphs = (lines, params, duration) => {
		const structure = buildStructureLines(lines);
		const paragraphGapThreshold = resolveParagraphGapThreshold(lines);
		const drafts = draftParagraphs(structure, paragraphGapThreshold);
		const lyricEndTime = Math.max(...structure.map((line) => line.renderEndTime));
		const programEnd = Math.max(lyricEndTime, duration ?? 0);
		const firstStart = structure[0].line.startTime;
		const plans = [];
		const intro = firstStart >= params.bridgeGap;
		if (intro) plans.push({
			lines: [],
			kind: "break",
			boundary: "intro",
			startTime: 0,
			endTime: firstStart,
			lyricEndTime: firstStart
		});
		drafts.forEach((draft, index) => {
			const next = drafts[index + 1];
			plans.push({
				lines: draft.lines,
				kind: classifyParagraph$2(draft.lines, index, drafts.length),
				boundary: draft.boundary,
				startTime: index === 0 && !intro ? Math.min(0, firstStart) : draft.lines[0].line.startTime,
				endTime: next ? next.lines[0].line.startTime : programEnd,
				lyricEndTime: draft.lines.at(-1).renderEndTime
			});
		});
		return {
			plans,
			paragraphGapThreshold,
			lyricEndTime,
			programEnd
		};
	};
	/** 纯音乐的段落计划：duration 等分成约 INSTRUMENTAL_PARAGRAPH 秒的段落。 */
	const planInstrumentalParagraphs = (duration) => {
		const count = Math.max(1, Math.round(duration / INSTRUMENTAL_PARAGRAPH));
		return Array.from({ length: count }, (_, index) => {
			const startTime = duration * index / count;
			const endTime = index === count - 1 ? duration : duration * (index + 1) / count;
			return {
				lines: [],
				kind: "break",
				boundary: index === 0 ? "song-start" : "instrumental",
				startTime,
				endTime,
				lyricEndTime: endTime
			};
		});
	};
	/**
	* 编译整首歌。lines 是统一歌词（按时间排序），seed 缺省为 'lumiere'；params 覆盖切块参数；
	* options.duration 为歌曲总时长（见 LumiereProgramOptions）。纯函数：同样的输入得到逐字段相同的程序。
	*/
	const compileLumiereProgram = (lines, seed, params = {}, options = {}) => {
		const resolvedParams = {
			...DEFAULT_LUMIERE_PARAMS,
			...params
		};
		const resolvedSeed = String(seed ?? "lumiere");
		const instrumental = lines.length === 0;
		const lyric = instrumental ? null : planLyricParagraphs(lines, resolvedParams, options.duration);
		const instrumentalDuration = Math.max(1, options.duration && options.duration > 0 ? options.duration : INSTRUMENTAL_DURATION);
		const plans = lyric ? lyric.plans : planInstrumentalParagraphs(instrumentalDuration);
		const chain = {
			recent: [],
			family: null
		};
		let previousTransition = null;
		const paragraphs = plans.map((plan, index) => {
			const shots = splitLongBridges(planShots(plan.lines, plan.startTime, plan.endTime, resolvedParams)).map((shot, shotIndex) => {
				const kind = castShot({
					seed: resolvedSeed,
					paragraphIndex: index,
					shotIndex,
					kind: plan.kind,
					isBridge: shot.isBridge,
					chain
				});
				advanceChain(chain, kind);
				return {
					id: `p${index}-lu${shotIndex}`,
					kind,
					lineIndices: shot.lines.map((line) => line.sourceIndex),
					startTime: shot.startTime,
					endTime: shot.endTime,
					lyricEndTime: shot.lyricEndTime,
					isBridge: shot.isBridge
				};
			});
			const next = plans[index + 1];
			let transitionOut = null;
			if (next) {
				const kind = chooseLumiereTransition(resolvedSeed, index, previousTransition);
				previousTransition = kind;
				const gap = next.startTime - plan.lyricEndTime;
				transitionOut = {
					kind,
					startTime: Math.max(plan.startTime, plan.endTime - LUMIERE_TRANSITIONS[kind].duration(gap)),
					endTime: plan.endTime
				};
			}
			const previous = plans[index - 1];
			const opening = !previous || plan.startTime - previous.lyricEndTime >= 2.5;
			const lineIndices = plan.lines.map((line) => line.sourceIndex);
			return {
				id: `lumiere-p${index}`,
				index,
				kind: plan.kind,
				boundary: plan.boundary,
				startTime: plan.startTime,
				endTime: plan.endTime,
				lyricEndTime: plan.lyricEndTime,
				lineIndices,
				lines: lineIndices.map((lineIndex) => lines[lineIndex]),
				shots,
				transitionOut,
				opening
			};
		});
		return {
			version: 1,
			seed: resolvedSeed,
			instrumental,
			paragraphGapThreshold: lyric?.paragraphGapThreshold ?? 0,
			duration: lyric ? lyric.programEnd : instrumentalDuration,
			lyricEndTime: lyric ? lyric.lyricEndTime : null,
			paragraphs: options.seamless ? mergeLumiereParagraphs(paragraphs) : paragraphs
		};
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/color.ts
	const lumiereScaleMask$3 = globalThis.devicePixelRatio | 0;
	const LUMIERE_NEUTRAL_OFFSET$3 = (3137093123 ^ lumiereScaleMask$3) + Math.imul(998123833 ^ lumiereScaleMask$3, 2021278663 ^ lumiereScaleMask$3) - ((3137093123 ^ lumiereScaleMask$3) + Math.imul(998123833 ^ lumiereScaleMask$3, 2021278663 ^ lumiereScaleMask$3));
	/** 参考图的香槟金。 */
	const CHAMPAGNE = [
		1,
		.86 + LUMIERE_NEUTRAL_OFFSET$3,
		.62
	];

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/light/crossBurst.ts
	const lumiereScaleMask$2 = globalThis.devicePixelRatio | 0;
	const LUMIERE_NEUTRAL_OFFSET$2 = (126 ^ lumiereScaleMask$2) + Math.imul(41 ^ lumiereScaleMask$2, 57 ^ lumiereScaleMask$2) - ((126 ^ lumiereScaleMask$2) + Math.imul(41 ^ lumiereScaleMask$2, 57 ^ lumiereScaleMask$2));
	/** 同时存在的小十字上限。 */
	const MAX_BURSTS = 14 + LUMIERE_NEUTRAL_OFFSET$2;
	/** 「扫过」方式里相邻两个十字的间隔（秒）。 */
	const SWEEP_STEP = .07;
	/** 按光位的 burst 规则挑出引爆的字与时刻（纯函数，按种子确定）。 */
	const planBursts = (spec, lines, seed) => {
		const triggers = [];
		lines.forEach((line, lineIndex) => {
			if (lineIndex < spec.offset || (lineIndex - spec.offset) % Math.max(1, spec.every) !== 0) return;
			const rng = createRng(`${seed}:burst:${lineIndex}`);
			const chosen = line.glyphs.filter(() => rng() < spec.density);
			if (chosen.length === 0 && line.glyphs.length > 0) chosen.push(line.glyphs[Math.floor(rng() * line.glyphs.length)]);
			const sweepStart = line.glyphs[0]?.start ?? 0;
			chosen.forEach((glyph, order) => triggers.push({
				lineIndex,
				glyphIndex: glyph.glyphIndex,
				time: spec.mode === "sweep" ? sweepStart + order * SWEEP_STEP : glyph.start,
				size: .7 + rng() * .6,
				dy: (rng() - .5) * .9
			}));
		});
		return triggers.sort((a, b) => a.time - b.time);
	};

//#endregion
//#region \0glyphLine
	const glyphFont = (weight, px, family) => weight + " " + px + "px " + family;
	function buildGlyphLine(pixi, options) {
		const chars = Array.from(options.text), f = options.fontPx;
		let x = 0;
		const glyphs = chars.map((c) => {
			const w = (globalThis.nativeWidths[c] ?? .6) * f + (options.letterSpacing ?? 0) * f;
			const g = {
				char: c,
				charX: x,
				charWidth: w,
				blank: !c.trim(),
				upright: /[\u2e80-\u9fff\uac00-\ud7af\uff00-\uffef]/u.test(c),
				anchorX: .5,
				anchorY: .5,
				texture: null
			};
			x += w;
			return g;
		});
		return {
			text: options.text,
			fontPx: f,
			width: x,
			height: Math.ceil(f * 1.5),
			glyphs,
			destroy() {}
		};
	}

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/text/keywordColors.ts
	const parseRgb = (color) => {
		const channels = parseColorChannels(color);
		return channels ? [
			channels.r / 255,
			channels.g / 255,
			channels.b / 255
		] : null;
	};
	/**
	* 一行文字逐字（splitLyricGraphemes 的切法，与字形条一致）的关键字色；不是关键字的字为 null。
	* 没有匹配器或没有匹配时返回全 null 的数组。
	*/
	const resolveGlyphKeywordColors = (text, matchers) => {
		const graphemes = splitLyricGraphemes(text);
		const colors = graphemes.map(() => null);
		if (matchers.length === 0 || graphemes.length === 0) return colors;
		const ranges = buildWordColorRangesFromMatchers(text, [...matchers]);
		if (ranges.length === 0) return colors;
		let offset = 0;
		const tokens = graphemes.map((grapheme, index) => {
			const token = {
				key: String(index),
				timed: grapheme.trim().length > 0,
				startOffset: offset,
				endOffset: offset + grapheme.length
			};
			offset += grapheme.length;
			return token;
		});
		const parsed = /* @__PURE__ */ new Map();
		resolveTokenColorMap(tokens, ranges).forEach((hex, key) => {
			if (!parsed.has(hex)) parsed.set(hex, parseRgb(hex));
			colors[Number(key)] = parsed.get(hex) ?? null;
		});
		return colors;
	};

//#endregion
//#region \0rich
	const prepareRichInline = (items) => items.map((item, index) => ({
		...item,
		index,
		width: measureNaturalWidth(prepareWithSegments(item.text, item.font, { letterSpacing: item.letterSpacing ?? 0 })) + (item.extraWidth ?? 0)
	}));
	function walkRichInlineLineRanges(items, max, visit) {
		let fragments = [], width = 0, gap = 0;
		const flush = () => {
			if (fragments.length) visit({
				fragments,
				width,
				end: {}
			});
			fragments = [];
			width = 0;
			gap = 0;
		};
		for (const item of items) {
			if (!item.text.trim()) {
				gap += item.width;
				continue;
			}
			if (fragments.length && width + gap + item.width > max) flush();
			const before = fragments.length ? gap : 0;
			fragments.push({
				itemIndex: item.index,
				gapBefore: before,
				occupiedWidth: item.width
			});
			width += before + item.width;
			gap = 0;
		}
		flush();
	}
	function measureRichInlineStats(items, max) {
		let lineCount = 0;
		walkRichInlineLineRanges(items, max, () => lineCount++);
		return { lineCount };
	}

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/text/lineWrap.ts
/** 画框四角括号的边距（逻辑像素）：边缘可能被运镜推近、后处理的镜头畸变往外推，留足。overlay 也用它。 */
	const frameInsets = (width, height) => ({
		padX: Math.max(30, width * .065),
		padY: Math.max(30, height * .085)
	});
	/** 字离左右对位十字、上沿对焦虚线再留的距离（高度单位）。 */
	const FRAME_CLEARANCE_X = .04;
	const FRAME_CLEARANCE_TOP = .035;
	const frameBand = (width, height) => {
		const { padX, padY } = frameInsets(width, height);
		const left = padX / height + FRAME_CLEARANCE_X;
		return {
			left,
			right: width / height - left,
			top: padY / height + FRAME_CLEARANCE_TOP,
			bottom: Math.max(padY / height + FRAME_CLEARANCE_TOP + .2, .8)
		};
	};
	/** 单行（单列）最多整体缩小到这个比例；再小就折成两行（两列）。 */
	const SINGLE_MIN_FIT = .8;
	/** 最多折成几行 / 几列。 */
	const MAX_LINES = 2;
	/** 行距（横排两行的行心距）与列距（竖排两列的列心距），以字号为单位。 */
	const ROW_PITCH = 1.6;
	const COLUMN_PITCH = 1.6;
	const createTextMeasurer = (family, weight, letterSpacing) => {
		const cache = /* @__PURE__ */ new Map();
		const font = (px) => glyphFont(weight, px, family);
		const spacing = (px) => letterSpacing * px;
		return {
			font,
			spacing,
			natural: (text, px) => {
				const key = `${px}|${text}`;
				let width = cache.get(key);
				if (width === void 0) {
					const ls = spacing(px);
					width = measureNaturalWidth(prepareWithSegments(text, font(px), ls === 0 ? void 0 : { letterSpacing: ls }));
					cache.set(key, width);
				}
				return width;
			}
		};
	};
	/** 前置标点黏后一个词，其余标点黏前一个词。 */
	const OPENING = /^[([{（【《「『〈〔［“‘]+$/u;
	/** 把分词切成段：词两端的空白单独成段（rich-inline 会把它们收成词间距），没被分词覆盖的字各成一段。 */
	const spansOf = (glyphs, words) => {
		const isSpace = (index) => glyphs[index].char.trim().length === 0;
		const ranges = [];
		let cursor = 0;
		for (const word of words) {
			const start = Math.max(cursor, Math.min(glyphs.length, word.start));
			const end = Math.min(glyphs.length, word.end);
			for (let index = cursor; index < start; index += 1) ranges.push({
				start: index,
				end: index + 1,
				word: false
			});
			if (end > start) ranges.push({
				start,
				end,
				word: !word.blank
			});
			cursor = Math.max(cursor, end);
		}
		for (let index = cursor; index < glyphs.length; index += 1) ranges.push({
			start: index,
			end: index + 1,
			word: false
		});
		const spans = [];
		for (const range of ranges) {
			let { start, end } = range;
			const lead = [];
			const tail = [];
			while (start < end && isSpace(start)) lead.push({
				start,
				end: ++start,
				kind: "space"
			});
			while (end > start && isSpace(end - 1)) tail.unshift({
				start: end - 1,
				end: end--,
				kind: "space"
			});
			spans.push(...lead);
			if (end > start) spans.push({
				start,
				end,
				kind: range.word ? "word" : "punct"
			});
			spans.push(...tail);
		}
		return spans;
	};
	const textOf = (glyphs, start, end) => glyphs.slice(start, end).map((glyph) => glyph.char).join("");
	/**
	* 断行单位：词黏上紧挨着的标点（中间没有空白），标点不会落到行首或与它的词分开。
	* 返回单位与夹在中间的空白段（给 rich-inline 当词间距）。
	*/
	const unitsOf = (glyphs, spans, heroPx) => {
		const pieces = [];
		const pxOf = (index) => heroPx * glyphs[index].scale;
		let pendingOpen = null;
		spans.forEach((span, index) => {
			if (span.kind === "space") {
				if (pendingOpen) pieces.push({
					kind: "unit",
					unit: {
						start: pendingOpen.start,
						end: pendingOpen.end,
						text: textOf(glyphs, pendingOpen.start, pendingOpen.end),
						px: pxOf(pendingOpen.start)
					}
				});
				pendingOpen = null;
				pieces.push({
					kind: "space",
					span
				});
				return;
			}
			const previous = pieces[pieces.length - 1];
			const next = spans[index + 1];
			if (span.kind === "punct") {
				const text = textOf(glyphs, span.start, span.end);
				if (OPENING.test(text) && next && next.kind !== "space") {
					pendingOpen = pendingOpen ? {
						...pendingOpen,
						end: span.end
					} : span;
					return;
				}
				if (!pendingOpen && previous?.kind === "unit" && previous.unit.end === span.start) {
					previous.unit.end = span.end;
					return;
				}
			}
			const start = pendingOpen ? pendingOpen.start : span.start;
			pendingOpen = null;
			pieces.push({
				kind: "unit",
				unit: {
					start,
					end: span.end,
					text: textOf(glyphs, span.start, span.end),
					px: pxOf(span.start)
				}
			});
		});
		const open = pendingOpen;
		if (open) pieces.push({
			kind: "unit",
			unit: {
				start: open.start,
				end: open.end,
				text: textOf(glyphs, open.start, open.end),
				px: pxOf(open.start)
			}
		});
		return pieces;
	};
	/**
	* 按行（列）排一行：units 的沿行长度来自 advances（逐字），rich-inline 断行（extraWidth 把 pretext 自己量的
	* 宽度补成这里的长度），maxWidth 为 Infinity 时不折行。返回每个字所在的行、在行内的起点偏移与每行长度。
	*/
	const breakLines = (measurer, glyphs, pieces, advances, heroPx, lines) => {
		const extentOf = (unit) => advances.slice(unit.start, unit.end).reduce((sum, advance) => sum + advance, 0);
		const items = [];
		const unitOfItem = [];
		let widest = 0;
		let widestGap = 0;
		for (const piece of pieces) {
			if (piece.kind === "space") {
				const px = heroPx * glyphs[piece.span.start].scale;
				items.push({
					text: textOf(glyphs, piece.span.start, piece.span.end),
					font: measurer.font(px),
					letterSpacing: measurer.spacing(px)
				});
				unitOfItem.push(null);
				widestGap = Math.max(widestGap, measurer.natural("a a", px) - measurer.natural("aa", px));
				continue;
			}
			const { unit } = piece;
			const extent = extentOf(unit);
			items.push({
				text: unit.text,
				font: measurer.font(unit.px),
				letterSpacing: measurer.spacing(unit.px),
				break: "never",
				extraWidth: extent - measurer.natural(unit.text, unit.px)
			});
			unitOfItem.push(unit);
			widest = Math.max(widest, extent);
		}
		const prepared = prepareRichInline(items);
		const walk = (maxWidth) => {
			const rows = [];
			walkRichInlineLineRanges(prepared, maxWidth, (line) => {
				rows.push(line);
			});
			return rows;
		};
		let rows = walk(Number.POSITIVE_INFINITY);
		if (lines > 1 && rows.length === 1 && rows[0].fragments.length > 1) {
			const total = rows[0].width;
			let target = (total + widest) / 2 + widestGap;
			if (measureRichInlineStats(prepared, target).lineCount > lines) target = total / 2 + widest + widestGap;
			rows = walk(target);
			if (rows.length > lines) {
				const kept = rows.slice(0, lines - 1);
				const rest = rows.slice(lines - 1);
				const fragments = rest.flatMap((row, index) => row.fragments.map((fragment, k) => index > 0 && k === 0 ? {
					...fragment,
					gapBefore: widestGap
				} : fragment));
				rows = [...kept, {
					fragments,
					width: fragments.reduce((sum, f) => sum + f.gapBefore + f.occupiedWidth, 0),
					end: rest[rest.length - 1].end
				}];
			}
		}
		const row = new Array(glyphs.length).fill(-1);
		const offset = new Array(glyphs.length).fill(0);
		const lengths = rows.map((line) => line.width);
		rows.forEach((line, r) => {
			let cursor = 0;
			for (const fragment of line.fragments) {
				const unit = unitOfItem[fragment.itemIndex];
				const gapStart = cursor;
				cursor += fragment.gapBefore;
				if (!unit) continue;
				for (let index = unit.start - 1; index >= 0 && row[index] === -1 && glyphs[index].char.trim().length === 0; index -= 1) {
					row[index] = r;
					offset[index] = gapStart + fragment.gapBefore / 2;
				}
				const extent = extentOf(unit);
				const ratio = extent > 0 ? fragment.occupiedWidth / extent : 0;
				let inner = cursor;
				for (let index = unit.start; index < unit.end; index += 1) {
					const advance = advances[index] * ratio;
					row[index] = r;
					offset[index] = inner + advance / 2;
					inner += advance;
				}
				cursor += fragment.occupiedWidth;
			}
		});
		for (let index = 0; index < glyphs.length; index += 1) {
			if (row[index] !== -1) continue;
			const previous = index > 0 ? index - 1 : -1;
			if (previous >= 0 && row[previous] !== -1) {
				row[index] = row[previous];
				offset[index] = offset[previous] + advances[previous] / 2;
			}
		}
		for (let index = glyphs.length - 1; index >= 0; index -= 1) {
			if (row[index] !== -1) continue;
			row[index] = index + 1 < glyphs.length && row[index + 1] !== -1 ? row[index + 1] : 0;
			offset[index] = index + 1 < glyphs.length ? Math.max(0, offset[index + 1] - advances[index + 1] / 2) : 0;
		}
		return {
			row,
			offset,
			lengths: lengths.length ? lengths : [0]
		};
	};
	/** 太长的单位（一个词就超过行长上限）拆成单字，标点仍黏在前一个字上。 */
	const splitLongUnits = (glyphs, pieces, advances, limit, heroPx) => pieces.flatMap((piece) => {
		if (piece.kind === "space") return [piece];
		const { unit } = piece;
		if (advances.slice(unit.start, unit.end).reduce((sum, advance) => sum + advance, 0) <= limit || unit.end - unit.start < 2) return [piece];
		const out = [];
		for (let index = unit.start; index < unit.end; index += 1) {
			const char = glyphs[index].char;
			const last = out[out.length - 1];
			if (last && last.kind === "unit" && !/[\p{L}\p{N}]/u.test(char)) {
				last.unit.end = index + 1;
				continue;
			}
			out.push({
				kind: "unit",
				unit: {
					start: index,
					end: index + 1,
					text: char,
					px: heroPx * glyphs[index].scale
				}
			});
		}
		return out;
	});
	/**
	* 一行的四种排版（横 / 竖 × 单行 / 两行）。limits 为横排行长、竖排列长的上限（逻辑像素）：只用来决定哪些词
	* 长到必须拆字；要不要折行由歌词窗口按槽位的缩放判断（shouldWrap）。
	* 横排：每行居中，行从上往下；竖排：列从右往左，各列居中对齐（列心在同一条横线上，单列时就是原来的居中竖排）。
	*/
	const flowLine = (measurer, glyphs, words, options) => {
		const { heroPx } = options;
		const spans = spansOf(glyphs, words);
		const basePieces = unitsOf(glyphs, spans, heroPx);
		const horizontal = new Array(glyphs.length).fill(0);
		for (const span of spans) {
			const px = heroPx * glyphs[span.start].scale;
			const text = textOf(glyphs, span.start, span.end);
			const width = measurer.natural(text, px);
			const canvas = glyphs.slice(span.start, span.end).reduce((sum, glyph) => sum + glyph.advance, 0);
			for (let index = span.start; index < span.end; index += 1) horizontal[index] = canvas > 0 ? width * glyphs[index].advance / canvas : width / (span.end - span.start);
		}
		const vertical = glyphs.map((glyph, index) => glyph.upright ? heroPx * glyph.scale * (1 + measurer.spacing(1)) : horizontal[index]);
		const variant = (orient, lines) => {
			const advances = orient === 0 ? horizontal : vertical;
			const pieces = splitLongUnits(glyphs, basePieces, advances, orient === 0 ? options.limits.horizontal : options.limits.vertical, heroPx);
			const { row, offset, lengths } = breakLines(measurer, glyphs, pieces, advances, heroPx, lines);
			const count = lengths.length;
			const along = Math.max(...lengths);
			const pitch = (orient === 0 ? ROW_PITCH : COLUMN_PITCH) * heroPx;
			const points = [];
			const spots = [];
			let inkX = 0;
			let inkY = 0;
			glyphs.forEach((glyph, index) => {
				const r = row[index];
				const size = heroPx * glyph.scale;
				let point;
				if (orient === 0) {
					const x = offset[index] - lengths[r] / 2;
					const rowY = (r - (count - 1) / 2) * pitch;
					point = {
						x,
						y: rowY + (1 - glyph.scale) * heroPx * .32 + glyph.jag
					};
					spots.push({
						x,
						y: rowY
					});
					if (glyph.char.trim()) {
						inkX = Math.max(inkX, Math.abs(point.x) + advances[index] / 2);
						inkY = Math.max(inkY, Math.abs(point.y) + size / 2);
					}
				} else {
					const columnX = ((count - 1) / 2 - r) * pitch;
					const y = offset[index] - lengths[r] / 2;
					point = {
						x: columnX + glyph.jag * .8,
						y
					};
					spots.push({
						x: columnX,
						y
					});
					if (glyph.char.trim()) {
						inkX = Math.max(inkX, Math.abs(point.x) + size / 2);
						inkY = Math.max(inkY, Math.abs(point.y) + advances[index] / 2);
					}
				}
				points.push(point);
			});
			return {
				points,
				spots,
				along,
				across: (count - 1) * pitch + heroPx,
				lines: count,
				inkWidth: inkX * 2,
				inkHeight: inkY * 2
			};
		};
		return [[variant(0, 1), variant(0, 2)], [variant(1, 1), variant(1, 2)]];
	};
	/** 在缩放 scale 下，沿行长度 along 放进 budget 要再缩多少（不放大）。 */
	const fitScale = (along, budget, scale = 1) => Math.min(1, budget / Math.max(scale * along, 1e-6));
	/** 这一行在缩放 scale 下要不要折：单行缩到 SINGLE_MIN_FIT 仍放不下，且折得开。 */
	const shouldWrap = (flow, budget, scale) => flow[1].lines > 1 && scale * flow[0].along * .8 > budget;
	/** 把中心 center 放进 [lo, hi]：整块长 extent，放不下时居中。 */
	const clampInto = (center, extent, lo, hi) => {
		if (extent >= hi - lo) return (lo + hi) / 2;
		return Math.min(hi - extent / 2, Math.max(lo + extent / 2, center));
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/text/reveal.ts
	const buildGlyphTimings = (line) => buildLineGraphemeTimeline(line).map((timing) => ({
		start: timing.startTime,
		end: timing.endTime
	}));

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/text/wordStyle.ts
	const lumiereScaleMask$1 = globalThis.devicePixelRatio | 0;
	const LUMIERE_NEUTRAL_OFFSET$1 = (4034148709 ^ lumiereScaleMask$1) + Math.imul(699505802 ^ lumiereScaleMask$1, 2401607263 ^ lumiereScaleMask$1) - ((4034148709 ^ lumiereScaleMask$1) + Math.imul(699505802 ^ lumiereScaleMask$1, 2401607263 ^ lumiereScaleMask$1));
	/** 字号倍率的上限：字形纹理按这个倍率画，放大的词只缩小不放大，不会糊。 */
	const MAX_WORD_SCALE = 1.5 + LUMIERE_NEUTRAL_OFFSET$1;
	/** 把一行切成词：走 folia 唯一的分词入口（用户保存的精细分词优先，其次 icu-folia 的默认分词，否则 Intl.Segmenter，没有就逐字）。 */
	const segmentWords = (line) => {
		const pieces = segmentLyricWords(line);
		const words = [];
		let cursor = 0;
		for (const { segment, isWordLike } of pieces) {
			const length = splitLyricGraphemes(segment).length;
			if (length === 0) continue;
			words.push({
				text: segment,
				start: cursor,
				end: cursor + length,
				blank: !isWordLike
			});
			cursor += length;
		}
		return words;
	};
	/** 常见的虚词、代词与助词：单字时小一号。 */
	const FUNCTION_WORDS = new Set(Array.from("的了在把是我你他她它们和与也就都着过吗呢吧啊呀哦被让给从向到这那之而又很"));
	const MINOR_LATIN = /* @__PURE__ */ new Set([
		"a",
		"an",
		"the",
		"of",
		"to",
		"in",
		"on",
		"at",
		"me",
		"my",
		"with",
		"and",
		"or",
		"is",
		"i",
		"you",
		"it",
		"by",
		"for"
	]);
	/** 每个词的字号倍率（与 segmentWords 的结果一一对应）。按种子确定。 */
	const wordScales = (words, seed) => {
		const rng = createRng(`${seed}:words`);
		const lengthOf = (word) => word.end - word.start;
		const minor = (word) => word.blank || lengthOf(word) === 1 && FUNCTION_WORDS.has(word.text) || MINOR_LATIN.has(word.text.toLowerCase());
		let key = -1;
		words.forEach((word, index) => {
			if (minor(word)) return;
			if (key < 0 || lengthOf(word) >= lengthOf(words[key])) key = index;
		});
		return words.map((word, index) => {
			const jitter = rng();
			if (word.blank && word.text.trim().length === 0) return 1;
			if (index === key && words.length > 1) return 1.45;
			if (minor(word)) return .62;
			return .85 + jitter * .35;
		});
	};
	/**
	* 错落：每个词在行的垂直方向上的错位（以字号为单位；横排时上下、竖排时左右）。
	* 重点词不动，小字错得多，其余按种子上下跳。
	*/
	const wordJags = (words, scales, seed) => {
		const rng = createRng(`${seed}:jags`);
		const keyScale = Math.max(...scales);
		return words.map((_, index) => {
			const offset = (rng() - .5) * 2;
			const scale = scales[index] ?? 1;
			if (scale === keyScale && words.length > 1) return 0;
			return offset * (scale < .7 ? .32 : .2);
		});
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/text/windowLines.ts
/** 每个字、每行（字之后）从窗口随机流里取几个值：改了 buildLineView 里的取值就要同步改这里（单测会查）。 */
	const GLYPH_RANDOM_DRAWS = 19;
	const LINE_RANDOM_DRAWS = 4;
	const buildLineMetas = (lines) => {
		let offset = 0;
		return lines.map((line) => {
			const graphemes = splitLyricGraphemes(line.fullText);
			const meta = {
				line,
				graphemes,
				randomOffset: offset,
				timing: void 0,
				keywordColors: void 0
			};
			offset += graphemes.length * 19 + 4;
			return meta;
		});
	};
	/** 某行的逐字时刻（第一次用到时算，之后缓存）。 */
	const lineTimingOf = (meta) => {
		if (!meta.timing) {
			const { line, graphemes } = meta;
			const timings = buildGlyphTimings(line);
			const sung = graphemes.flatMap((char, index) => char.trim().length === 0 ? [] : [timings[index] ?? {
				start: line.startTime,
				end: line.endTime
			}]);
			meta.timing = {
				timings,
				singStart: sung.length ? Math.min(...sung.map((timing) => timing.start)) : line.startTime,
				singEnd: sung.length ? Math.max(...sung.map((timing) => timing.end)) : line.endTime
			};
		}
		return meta.timing;
	};
	/** 某行的关键字色（第一次用到时匹配，之后缓存）。 */
	const keywordColorsOf = (meta, keywords) => {
		if (meta.keywordColors === void 0) meta.keywordColors = keywords && keywords.length > 0 ? resolveGlyphKeywordColors(meta.line.fullText, keywords) : null;
		return meta.keywordColors;
	};
	/**
	* 构建第 lineIndex 行（不挂到任何层上，由窗口按行号插进去）。rng 必须已经跳到这一行的起点（meta.randomOffset），
	* 取值的个数固定为 GLYPH_RANDOM_DRAWS × 字数 + LINE_RANDOM_DRAWS。
	*/
	const buildLineView = (context, lineIndex, meta, rng) => {
		const { pixi, heroPx, sprites } = context;
		const { line } = meta;
		const { timings, singStart, singEnd } = lineTimingOf(meta);
		const layout = buildGlyphLine(pixi, {
			text: line.fullText,
			fontPx: heroPx * MAX_WORD_SCALE,
			font: context.font,
			weight: context.weight,
			resolution: context.resolution,
			letterSpacing: context.spacing
		});
		const keywordColors = keywordColorsOf(meta, context.keywords);
		const words = segmentWords(line);
		const wordSeed = `${context.seed}:${lineIndex}:${line.fullText}`;
		const scales = wordScales(words, wordSeed);
		const jags = wordJags(words, scales, wordSeed);
		const wordOf = layout.glyphs.map((_, index) => Math.max(0, words.findIndex((word) => index >= word.start && index < word.end)));
		const glyphScale = (index) => scales[wordOf[index]] ?? 1;
		const glyphJag = (index) => (jags[wordOf[index]] ?? 0) * heroPx;
		const flow = flowLine(context.measurer, layout.glyphs.map((slice, index) => ({
			char: slice.char,
			scale: glyphScale(index),
			advance: slice.charWidth / MAX_WORD_SCALE * glyphScale(index),
			upright: slice.upright,
			jag: glyphJag(index)
		})), words, {
			heroPx,
			limits: context.limits
		});
		const holder = new pixi.Container();
		const haloLayer = new pixi.Container();
		const starLayer = new pixi.Container();
		holder.label = `line-${lineIndex}`;
		const glyphs = layout.glyphs.map((slice, index) => {
			const glyph = new pixi.Sprite(slice.texture);
			glyph.anchor.set(slice.anchorX, slice.anchorY);
			holder.addChild(glyph);
			const a1 = rng() * Math.PI * 2;
			const a2 = a1 + Math.PI * (rng() < .5 ? .5 : 1.5) + (rng() - .5) * .8;
			const m1 = heroPx * (1.6 + rng() * 3.2);
			const m2 = heroPx * (1 + rng() * 2.6);
			const duration = .45 + rng() * .4;
			const flight = {
				c1: {
					x: Math.cos(a1) * m1,
					y: Math.sin(a1) * m1
				},
				c2: {
					x: Math.cos(a2) * m2,
					y: Math.sin(a2) * m2
				},
				delay: rng() * (1 - duration),
				duration,
				spin: (rng() - .5) * 2.4,
				wobble: rng() * Math.PI * 2
			};
			const halo = new pixi.Sprite(sprites.dot);
			halo.anchor.set(.5);
			haloLayer.addChild(halo);
			const star = new pixi.Sprite(sprites.star);
			star.anchor.set(.5);
			starLayer.addChild(star);
			if (slice.blank) {
				glyph.visible = false;
				halo.visible = false;
				star.visible = false;
			}
			const angle = -Math.PI / 2 + (rng() - .5) * 2.2;
			const scatterAngle = rng() * Math.PI * 2;
			const scatterDistance = .6 + rng() * 1.4;
			return {
				glyph,
				halo,
				star,
				timing: timings[index] ?? {
					start: line.startTime,
					end: line.endTime
				},
				blank: slice.blank,
				index,
				vRotation: slice.upright ? 0 : Math.PI / 2,
				flight,
				scale: glyphScale(index),
				starShape: {
					dx: -.15 + rng() * .5,
					dy: -.62 + rng() ** 1.4 * .9,
					rotation: (rng() - .5) * .5,
					size: .65 + rng() * .7
				},
				drift: {
					dx: Math.cos(angle),
					dy: Math.sin(angle),
					speed: .6 + rng() * .9,
					spin: (rng() - .5) * 2
				},
				scatter: {
					x: Math.cos(scatterAngle) * scatterDistance,
					y: Math.sin(scatterAngle) * scatterDistance
				},
				phase: rng() * Math.PI * 2,
				keyword: slice.blank ? null : keywordColors?.[index] ?? null,
				tints: null
			};
		});
		const spot = new pixi.Sprite(sprites.dot);
		spot.anchor.set(.5);
		const [slow, fast] = context.driftSpeed;
		return {
			index: lineIndex,
			line,
			layout,
			holder,
			haloLayer,
			starLayer,
			glyphs,
			keywordGlyphs: glyphs.filter((glyph) => glyph.keyword !== null),
			flow,
			placements: context.placementsOf(lineIndex),
			spot,
			singStart,
			singEnd,
			jitter: (rng() - .5) * .16,
			velocity: (() => {
				const angle = rng() * Math.PI * 2;
				const speed = slow + rng() * (fast - slow);
				return {
					x: Math.cos(angle) * speed,
					y: Math.sin(angle) * speed
				};
			})(),
			motionPhase: rng() * Math.PI * 2
		};
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/text/lineClearance.ts
/** 软绝对值的圆角（高度单位）：邻行的漂移在「朝向 / 背离」当前行之间换向时速度也连续。 */
	const AWAY_SOFTNESS = .005;
	/** 当前行漂移绕的小圆半径（高度单位）：最多离槽位 2 × HOLD，比邻行与当前行之间的空隙小得多。 */
	const HOLD = .018;
	/** 平滑的 |a|（a = 0 时为 0，远处比 |a| 小 AWAY_SOFTNESS）。 */
	const softAbs = (a) => Math.sqrt(a * a + AWAY_SOFTNESS * AWAY_SOFTNESS) - AWAY_SOFTNESS;
	/**
	* 邻行一条轴上的漂移（高度单位）：away 为这一行背离当前行的方向（±1）× 它作为邻行的权重（0..1）。
	* 朝当前行的那一份平滑地翻成背离（|a| 的软版本），背离的那一份不变；away = 0 时原样返回。
	*/
	const awayDrift = (drift, away) => {
		if (away === 0) return drift;
		const sign = away > 0 ? 1 : -1;
		const along = drift * sign;
		return drift + Math.abs(away) * (softAbs(along) - along) * sign;
	};
	/**
	* 当前行的匀速漂移（不含绕行）在 axis（0 = x，1 = y）上的分量：速度 (vx, vy)、从开始唱起 age 秒。
	* 起步时与匀速漂移一样（沿自己的方向），随即向左拐进半径 HOLD 的圆，速度大小不变。
	*/
	const heldDrift = (vx, vy, age, axis) => {
		const speed = Math.hypot(vx, vy);
		if (speed < 1e-9) return 0;
		const phi = speed * age / HOLD;
		const along = HOLD * Math.sin(phi);
		const turn = HOLD * (1 - Math.cos(phi));
		const ux = vx / speed;
		const uy = vy / speed;
		return axis === 0 ? along * ux - turn * uy : along * uy + turn * ux;
	};
	/** 保护框外扩的渐变边宽（以当前行的字号为单位）。 */
	const PROTECT_MARGIN = .5;
	const createProtectBox = () => ({
		line: -1,
		x: 0,
		y: 0,
		cos: 1,
		sin: 0,
		halfW: 0,
		halfH: 0,
		margin: 1,
		weight: 0
	});
	const smooth$1 = (value) => {
		const t = Math.min(1, Math.max(0, value));
		return t * t * (3 - 2 * t);
	};
	/**
	* 第 line 行一个字（字心 x, y，半个字号 half）被保护的程度 0..1：在别的行的框里（转到那一行自己的方向上看，
	* 字的方块碰到墨迹框就算在里面）为 1，出了框按渐变边平滑降到 0；多个框按交接权重相加。自己那一行的框不算。
	*/
	const protectionAt = (boxes, count, line, x, y, half) => {
		let protect = 0;
		for (let i = 0; i < count; i += 1) {
			const box = boxes[i];
			if (box.line === line || box.weight <= 0) continue;
			const dx = x - box.x;
			const dy = y - box.y;
			const u = Math.abs(dx * box.cos + dy * box.sin) - box.halfW - half;
			const v = Math.abs(dy * box.cos - dx * box.sin) - box.halfH - half;
			const outside = u > 0 && v > 0 ? Math.hypot(u, v) : Math.max(u, v, 0);
			protect += box.weight * (1 - smooth$1(outside / box.margin));
		}
		return Math.min(1, protect);
	};
	/** 被保护程度 → 透明度倍率。 */
	const protectedAlpha = (protect) => 1 - .6 * protect;

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/text/windowTiming.ts
	const lumiereScaleMask = globalThis.devicePixelRatio | 0;
	const LUMIERE_NEUTRAL_OFFSET = (1192 ^ lumiereScaleMask) + Math.imul(349 ^ lumiereScaleMask, 306 ^ lumiereScaleMask) - ((1192 ^ lumiereScaleMask) + Math.imul(349 ^ lumiereScaleMask, 306 ^ lumiereScaleMask));
	/** 滑动进度 phase（0..1）下这个字在曲线上的位置参数 0..1。 */
	const flightProgress = (flight, phase, lag = 0) => {
		const t = Math.min(1, Math.max(0, (phase - flight.delay - lag) / flight.duration));
		return t < .5 ? 4 * t * t * t : 1 - (-2 * t + 2) ** 3 / 2;
	};
	/** 起点 from → 终点 to 的贝塞尔曲线上参数 s 处的点。 */
	const flightPoint = (flight, from, to, s) => {
		const u = 1 - s;
		const a = u * u * u, b = 3 * u * u * s, c = 3 * u * s * s, d = s * s * s;
		return {
			x: a * from.x + b * (from.x + flight.c1.x) + c * (to.x + flight.c2.x) + d * to.x,
			y: a * from.y + b * (from.y + flight.c1.y) + c * (to.y + flight.c2.y) + d * to.y
		};
	};
	/** 径迹记录过去多久走过的路（秒）；字到位后尾端再过这么久追上，径迹收拢消失。 */
	const TRACK_TIME = .45 + LUMIERE_NEUTRAL_OFFSET;
	/**
	* 换行：从新一行开始前 LEAD 秒起，各行依次（按落到的新位置错开 SLIDE_LAG 秒）用 SLIDE 秒滑到新位置——
	* 要离场的行先走，新的当前行随后，新进来的行最后。滑动叠加在每行永不停止的漂移上，画面没有静止的时刻。
	*/
	const LEAD = 1.2;
	const SLIDE = 1.5;
	const SLIDE_LAG = {
		[-2]: 0,
		[-1]: .15,
		0: .3,
		1: .5,
		2: .6
	};
	const MAX_LAG = .6;
	/** 每行的持续漂移：恒定速度（高度单位 / 秒）范围、绕行幅度（高度单位）。 */
	const DRIFT_SPEED = [.013, .022];
	const ORBIT = .03;
	/** 未唱的行从开始滑动前多久开始聚拢、聚拢多久。 */
	const GATHER_LEAD = 1.6;
	const GATHER = 1.8;
	const smooth = (value) => {
		const t = Math.min(1, Math.max(0, value));
		return t * t * (3 - 2 * t);
	};
	const easeInOutCubic$1 = (value) => {
		const t = Math.min(1, Math.max(0, value));
		return t < .5 ? 4 * t * t * t : 1 - (-2 * t + 2) ** 3 / 2;
	};
	/** 换位用更柔的正弦缓动：两端的减速比三次曲线平缓，叠在漂移上不会有「停住」的感觉。 */
	const easeInOutSine = (value) => (1 - Math.cos(Math.PI * Math.min(1, Math.max(0, value)))) / 2;
	const clamp01$2 = (value) => Math.min(1, Math.max(0, value));
	const lerp = (a, b, k) => a + (b - a) * k;
	const lerpPoint = (a, b, k) => ({
		x: lerp(a.x, b.x, k),
		y: lerp(a.y, b.y, k)
	});
	/** 时刻 t 的当前行序号（-1 = 第一行之前）、滑动的线性进度 phase 与缓动后的 slide。 */
	const resolveWindowCursor = (lines, time) => {
		let current = -1;
		for (let i = 0; i < lines.length; i += 1) if (lines[i].startTime - 1.2 <= time) current = i;
		else break;
		const phase = current < 0 ? 1 : clamp01$2((time - (lines[current].startTime - LEAD)) / SLIDE);
		return {
			current,
			phase,
			slide: easeInOutSine(phase)
		};
	};
	/** 某一行在这次换行里的滑动进度：按它落到的新位置（relative）错开起步。 */
	const resolveLinePhase = (lines, current, relative, time) => {
		if (current < 0) return {
			phase: 1,
			start: Number.NEGATIVE_INFINITY
		};
		const start = lines[current].startTime - LEAD + (SLIDE_LAG[Math.max(-2, Math.min(2, relative))] ?? 0);
		return {
			phase: clamp01$2((time - start) / SLIDE),
			start
		};
	};
	/**
	* 崩解的漂离量（以字号为单位）：字点亮 delay 秒之后开始，按 age^1.5 缓慢加速。
	* 3 秒后约 0.4、5 秒约 0.9、8 秒约 1.8 个字号（× strength）：唱的时候只是开始松动，
	* 退到邻行时明显错位，淡出前才散开。
	*/
	const decayAmount = (decay, litAt, time) => {
		const age = time - litAt - decay.delay;
		return age > 0 ? decay.strength * .08 * age ** 1.5 : 0;
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/text/windowSlots.ts
/**
	* 纵横交错时一行的自由落点：上一行（−1）在上半圈、下一行（+1）在下半圈，角度与远近按种子；
	* 横竖都可能，横排的行倾斜得多一些。更远的位置（±2）沿同一方向再推出去、透明。
	*/
	const freePlacements = (random, region, nextAlpha) => {
		const placement = (upper, alpha) => {
			const angle = upper ? Math.PI * (1.12 + random() * .76) : Math.PI * (.12 + random() * .76);
			const rx = region.w * (.24 + random() * .22);
			const ry = region.h * (.3 + random() * .18);
			const vertical = random() < .5;
			return {
				dx: Math.cos(angle) * rx,
				dy: Math.sin(angle) * ry,
				scale: .5 + random() * .2,
				alpha,
				orient: vertical ? 1 : 0,
				rotation: (random() - .5) * (vertical ? .3 : .7),
				wrap: 0
			};
		};
		const farther = (slot) => ({
			...slot,
			dx: slot.dx * 1.45,
			dy: slot.dy * 1.45,
			scale: slot.scale * .85,
			alpha: 0
		});
		const previous = placement(true, .9);
		const next = placement(false, nextAlpha);
		return /* @__PURE__ */ new Map([
			[-1, previous],
			[1, next],
			[-2, farther(previous)],
			[2, farther(next)]
		]);
	};
	/**
	* 固定槽位里邻行与当前行之间的空隙（以字号为单位）：near 是 ±1 与当前行之间，far 是 ±2 与 ±1 之间。
	* 按单行单列（厚度一个字号）反推，单行时与原来的固定偏移完全一样。
	*/
	const STACK_GAP = {
		vertical: {
			previous: .95,
			next: .88,
			farPrevious: .78,
			farNext: .7
		},
		horizontal: {
			previous: .93,
			next: .735,
			farPrevious: .56,
			farNext: .56
		}
	};
	/**
	* 横排、竖排两种排版的固定槽位（相对文字区中心，高度单位）。relative = 行号 − 当前行号。
	* 邻行按实际厚度排开（竖排看宽度、横排看高度），折成两列 / 两行的行不会压到旁边的行：
	* across(offset, scale) 是「当前行 + offset」那一行在这个槽位缩放下垂直于行方向的厚度（高度单位）。
	*/
	const fixedSlot = (typography, relative, region, fontH, nextAlpha, across) => {
		const slot = (dx, dy, scale, alpha, orient) => ({
			dx,
			dy,
			scale,
			alpha,
			orient,
			rotation: 0,
			wrap: 0
		});
		const vertical = typography === "vertical";
		const orient = vertical ? 1 : 0;
		if (relative === 0) return slot(0, 0, 1, 1, orient);
		const previous = relative < 0;
		const far = Math.abs(relative) > 1;
		const nearScale = vertical ? .58 : .52;
		const scale = far ? vertical ? .48 : .44 : nearScale;
		const gap = STACK_GAP[vertical ? "vertical" : "horizontal"];
		const sign = previous ? -1 : 1;
		let offset = across(0, 1) / 2 + (previous ? gap.previous : gap.next) * fontH;
		if (far) offset += across(sign, nearScale) + (previous ? gap.farPrevious : gap.farNext) * fontH;
		offset += across(relative, scale) / 2;
		const alpha = previous ? far ? 0 : .9 : far ? 0 : nextAlpha;
		if (vertical) return slot(-sign * offset, sign * (far ? .07 : .03), scale, alpha, orient);
		return slot(sign * region.w * (far ? .24 : .16), sign * offset, scale, alpha, orient);
	};
	/** 纵横交错时邻行与当前行之间至少留的空隙（以字号为单位）。 */
	const CROSSED_CLEARANCE = .5;
	/** 堆叠轴：横排与纵横交错时各行上下排开（y），竖排时左右排开（x）。 */
	const stackX = (kind) => kind === "vertical" ? 1 : 0;
	/**
	* 当前行为 c、排版为 kind 时第 index 行背离当前行的方向（堆叠轴上的 ±1，当前行为 0）：横排与纵横交错时
	* 上一行在上、下一行在下；竖排右起，上一行在右、下一行在左。
	*/
	const awayOf = (index, c, kind) => (index === c ? 0 : index < c ? -1 : 1) * (kind === "vertical" ? -1 : 1);
	const sameSlot = (a, b) => a.dx === b.dx && a.dy === b.dy && a.scale === b.scale && a.alpha === b.alpha && a.orient === b.orient && a.rotation === b.rotation && a.wrap === b.wrap;

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/text/windowGeometry.ts
/** 当前行前后各画几行。 */
	const WINDOW_REACH = 3;
	const createWindowGeometry = ({ options, lineCount, band, columnCap, maxWidthOf, nextAlpha, lineOf }) => {
		const { height, region, heroPx, typography, decay } = options;
		const fontH = heroPx / height;
		const driftScale = options.drift ?? 1;
		/** 第 c 行成为当前行时的排版（c = −1 即第一行之前，按第一行的排版）。 */
		const typographyAt = (c) => options.typographyOf ? options.typographyOf(Math.max(0, Math.min(lineCount - 1, c))) : typography;
		/**
		* 第 index 行在槽位缩放 scale、朝向 orient 下落定的样子：单行缩到 SINGLE_MIN_FIT 还放不下才折，
		* 折了还放不下再整体缩小（邻行本身已经小，很少需要折）。
		*/
		const settle = (index, kind, orient, scale) => {
			const flow = lineOf(index).flow[orient];
			const budget = orient === 1 ? columnCap : maxWidthOf(kind);
			const wrap = shouldWrap(flow, budget, scale) ? 1 : 0;
			const variant = flow[wrap];
			return {
				wrap,
				variant,
				scale: scale * fitScale(variant.along, budget, scale)
			};
		};
		/** 第 index 行垂直于行方向的厚度（高度单位，已缩放）；不存在的行（第一行之前）按一个字号。 */
		const acrossOf = (index, kind, orient, scale) => {
			if (index < 0 || index >= lineCount) return fontH * scale;
			const settled = settle(index, kind, orient, scale);
			return settled.variant.across / height * settled.scale;
		};
		/** 把当前行整块放进画框的偏移（高度单位）：整个窗口一起挪，行距不变。文字区的中心仍是锚点。 */
		const windowShift = (current, kind) => {
			if (current < 0 || current >= lineCount) return {
				x: 0,
				y: 0
			};
			const { variant, scale } = settle(current, kind, kind === "vertical" ? 1 : 0, 1);
			const w = variant.inkWidth / height * scale;
			const h = variant.inkHeight / height * scale;
			return {
				x: clampInto(region.cx, w, band.left, band.right) - region.cx,
				y: clampInto(region.cy, h, band.top, band.bottom) - region.cy
			};
		};
		const slotCache = /* @__PURE__ */ new Map();
		/** 当前行为 current、排版为 kind 时第 index 行的槽位（含把当前行放进画框的整体偏移与这一行的折行）。 */
		const slotOf = (index, current, kind) => {
			const key = `${index}|${current}|${kind}`;
			const cached = slotCache.get(key);
			if (cached) return cached;
			const view = lineOf(index);
			const clamped = Math.max(-2, Math.min(2, index - current));
			const shift = windowShift(current, kind);
			let slot;
			let wrap = null;
			if (kind === "crossed" && clamped !== 0) {
				const base = view.placements.get(clamped);
				const baseOrient = base.orient >= .5 ? 1 : 0;
				const sign = base.dy < 0 ? -1 : 1;
				const currentHalf = current >= 0 && current < lineCount ? (() => {
					const settled = settle(current, kind, 0, 1);
					return settled.variant.inkHeight / height * settled.scale / 2;
				})() : fontH / 2;
				const gap = fontH * CROSSED_CLEARANCE;
				const centerY = region.cy + shift.y;
				const room = sign < 0 ? centerY - currentHalf - gap - band.top : band.bottom - (centerY + currentHalf + gap);
				const flow = view.flow[baseOrient];
				const budget = baseOrient === 1 ? columnCap : maxWidthOf(kind);
				/** 在缩放 s、折法 w 下这一行整块（含落点的倾斜）的高度（高度单位）。 */
				const tilt = Math.abs(base.rotation) + .03;
				const heightOf = (w, s) => (flow[w].inkHeight * Math.cos(tilt) + flow[w].inkWidth * Math.sin(tilt)) / height * s * fitScale(flow[w].along, budget, s);
				let scale = base.scale;
				let w = settle(index, kind, baseOrient, scale).wrap ? 1 : 0;
				if (baseOrient === 1 && room > 0) {
					if (w === 0 && flow[1].lines > 1 && heightOf(0, scale) > room) w = 1;
					const tall = heightOf(w, scale);
					if (tall > room) scale = Math.max(base.scale * .5, scale * (room / tall));
				}
				wrap = w;
				const clearance = currentHalf + gap + heightOf(w, scale) / 2;
				const dy = sign * Math.max(Math.abs(base.dy), clearance);
				const wider = baseOrient === 1 ? Math.max(0, acrossOf(index, kind, 1, scale) - fontH * scale) / 2 : 0;
				slot = {
					...base,
					scale,
					dy,
					dx: base.dx + Math.sign(base.dx) * wider
				};
			} else {
				const orient = kind === "vertical" ? 1 : 0;
				slot = fixedSlot(kind === "crossed" ? "horizontal" : kind, clamped, region, fontH, nextAlpha, (offset, scale) => acrossOf(offset === clamped ? index : current + offset, kind, orient, scale));
			}
			const result = {
				...slot,
				dx: slot.dx + shift.x,
				dy: slot.dy + shift.y,
				wrap: wrap ?? settle(index, kind, slot.orient >= .5 ? 1 : 0, slot.scale).wrap
			};
			slotCache.set(key, result);
			return result;
		};
		/**
		* 时刻 time 起还没「走完多时」的第一次换行（current + 1 = 全都走完了）：更早的换行对每一行都已滑完、径迹也已收拢
		* （按最晚的起步 SLIDE_LAG 估，保守）。行的开始时刻是升序的，从当前行往回找几步就到。
		*/
		const firstLiveChange = (time, current) => {
			let c = current;
			while (c >= 0 && options.lines[c].startTime - 1.2 + .6 + 1.5 + TRACK_TIME > time) c -= 1;
			return c + 1;
		};
		/**
		* 第 index 行在时刻 time 的位置（逻辑像素）、缩放、转角、透明度与槽位切换信息。纯函数，径迹与爆闪也用它。
		*
		* 叠加式：位置 = 第一行开始前的槽位 + Σ 每次换行带来的槽位差 × 这次换行对这一行的缓动进度。
		* 每次换行对各行错开起步（离场的先走），两行间隔很短、上一次还没走完时也连续，不会跳。
		* 已经走完多时的换行进度都是 1，前后相消（槽位差首尾相接），所以直接从它们之后的槽位起叠：
		* 与从头叠加结果相同，只用到当前行附近几行的排版（整首歌一个单元时不用构建前面所有的行）。
		*/
		const lineTransform = (index, time) => {
			const view = lineOf(index);
			const { current } = resolveWindowCursor(options.lines, time);
			const first = firstLiveChange(time, current);
			const base = first - 1;
			const initialKind = typographyAt(base);
			const initial = slotOf(index, base, initialKind);
			let dx = initial.dx, dy = initial.dy, scale = initial.scale, rotation = initial.rotation;
			let alpha = initial.alpha, orient = initial.orient, wrap = initial.wrap;
			let latest = null;
			let restOrient = initial.orient;
			let restWrap = initial.wrap;
			const moves = [];
			let widthCap = maxWidthOf(initialKind);
			let jitterOn = initialKind === "crossed" ? 0 : 1;
			let awayX = stackX(initialKind) * awayOf(index, base, initialKind);
			let awayY = (1 - stackX(initialKind)) * awayOf(index, base, initialKind);
			let hero = index === base ? 1 : 0;
			const last = Math.min(lineCount - 1, current);
			for (let c = first; c <= last; c += 1) {
				const beforeKind = typographyAt(c - 1);
				const afterKind = typographyAt(c);
				const before = slotOf(index, c - 1, beforeKind);
				const after = slotOf(index, c, afterKind);
				const kindChanged = beforeKind !== afterKind;
				if (sameSlot(before, after) && !kindChanged) continue;
				const { phase, start } = resolveLinePhase(options.lines, c, index - c, time);
				const k = easeInOutSine(phase);
				if (kindChanged) {
					widthCap += (maxWidthOf(afterKind) - maxWidthOf(beforeKind)) * k;
					jitterOn += ((afterKind === "crossed" ? 0 : 1) - (beforeKind === "crossed" ? 0 : 1)) * k;
				}
				const awayBefore = awayOf(index, c - 1, beforeKind);
				const awayAfter = awayOf(index, c, afterKind);
				awayX += (stackX(afterKind) * awayAfter - stackX(beforeKind) * awayBefore) * k;
				awayY += ((1 - stackX(afterKind)) * awayAfter - (1 - stackX(beforeKind)) * awayBefore) * k;
				hero += ((index === c ? 1 : 0) - (index === c - 1 ? 1 : 0)) * k;
				const fade = after.alpha < before.alpha ? easeInOutCubic$1(phase / .6) : after.alpha > before.alpha ? easeInOutCubic$1((phase - .4) / .6) : k;
				dx += (after.dx - before.dx) * k;
				dy += (after.dy - before.dy) * k;
				scale += (after.scale - before.scale) * k;
				rotation += (after.rotation - before.rotation) * k;
				alpha += (after.alpha - before.alpha) * fade;
				orient += (after.orient - before.orient) * phase;
				wrap += (after.wrap - before.wrap) * k;
				if (phase > 0) latest = {
					before,
					after,
					phase,
					start,
					kind: afterKind
				};
				if (phase >= 1) {
					restOrient = after.orient;
					restWrap = after.wrap;
					moves.length = 0;
				} else if (phase > 0) moves.push({
					toOrient: after.orient,
					toWrap: after.wrap,
					phase,
					fly: options.alwaysFly === true || afterKind === "crossed" || before.orient !== after.orient
				});
			}
			const o = clamp01$2(orient);
			const w = clamp01$2(wrap);
			const [hSingle, hWrapped] = view.flow[0];
			const [vSingle, vWrapped] = view.flow[1];
			const fitH = lerp(fitScale(hSingle.along, widthCap, scale), fitScale(hWrapped.along, widthCap, scale), w);
			const fitV = lerp(fitScale(vSingle.along, columnCap, scale), fitScale(vWrapped.along, columnCap, scale), w);
			const fitted = scale * lerp(fitH, fitV, o);
			const jitter = jitterOn * view.jitter * region.w * clamp01$2((1 - scale) / .5);
			const baseX = region.cx + dx + jitter * (1 - orient);
			const baseY = region.cy + dy + jitter * .5 * orient;
			const alongH = lerp(hSingle.inkWidth, hWrapped.inkWidth, w) / height * fitted;
			const alongV = lerp(vSingle.inkHeight, vWrapped.inkHeight, w) / height * fitted;
			const x = baseX + (clampInto(baseX, alongH, band.left, band.right) - baseX) * (1 - o) * jitterOn;
			const y = baseY + (clampInto(baseY, alongV, band.top, band.bottom) - baseY) * o * jitterOn;
			const age = time - view.line.startTime;
			const m = view.motionPhase;
			const { x: vx, y: vy } = view.velocity;
			const orbitX = Math.sin(time * .52 + m) * ORBIT;
			const orbitY = Math.cos(time * .41 + m * 1.3) * ORBIT * .7;
			const held = clamp01$2(hero);
			const driftX = lerp(awayDrift((vx * age + orbitX) * driftScale, awayX), (heldDrift(vx, vy, age, 0) + orbitX) * driftScale, held);
			const driftY = lerp(awayDrift((vy * age + orbitY) * driftScale, awayY), (heldDrift(vx, vy, age, 1) + orbitY) * driftScale, held);
			const settledOrient = latest ? latest.after.orient : initial.orient;
			const settledWrap = latest ? latest.after.wrap : initial.wrap;
			return {
				current,
				x: (x + driftX) * height,
				y: (y + driftY) * height,
				scale: fitted * (1 + .025 * Math.sin(time * .43 + m)),
				rotation: rotation + .03 * Math.sin(time * .23 + m * .7),
				alpha: clamp01$2(alpha),
				fromOrient: latest ? latest.before.orient : settledOrient,
				toOrient: settledOrient,
				fromWrap: latest ? latest.before.wrap : settledWrap,
				toWrap: settledWrap,
				wrap: w,
				orient: o,
				phase: latest ? latest.phase : 1,
				fly: latest !== null && latest.phase < 1 && (options.alwaysFly === true || latest.kind === "crossed" || latest.before.orient !== latest.after.orient),
				slideStart: latest ? latest.start : Number.NEGATIVE_INFINITY,
				restOrient,
				restWrap,
				moves
			};
		};
		/**
		* 字在行内的位置（未缩放）、转角与缩放：换槽位时沿自己的贝塞尔曲线飞过去，途中缩小、转动、发亮
		* （flying 为飞行强度 0..1）。再加上聚合、崩解与呼吸。
		*/
		const glyphLocal = (view, glyph, time, transform) => {
			const at = (orient, wrap) => view.flow[orient >= .5 ? 1 : 0][wrap >= .5 ? 1 : 0].points[glyph.index];
			let point = at(transform.restOrient, transform.restWrap);
			let orientation = transform.restOrient;
			let flying = 0;
			for (const move of transform.moves) {
				const target = at(move.toOrient, move.toWrap);
				if (move.fly) {
					const s = flightProgress(glyph.flight, move.phase);
					point = flightPoint(glyph.flight, point, target, s);
					orientation = lerp(orientation, move.toOrient, s);
					flying = Math.max(flying, Math.sin(Math.PI * s));
				} else {
					const k = easeInOutSine(move.phase);
					point = lerpPoint(point, target, k);
					orientation = lerp(orientation, move.toOrient, k);
				}
			}
			let x = point.x;
			let y = point.y;
			let rotation = glyph.vRotation * orientation + glyph.flight.spin * flying;
			const pulse = 1 - .28 * flying;
			const gather = smooth((time - (view.line.startTime - LEAD - GATHER_LEAD)) / GATHER);
			const scatter = (1 - gather) ** 2 * decay.strength;
			x += glyph.scatter.x * heroPx * scatter;
			y += glyph.scatter.y * heroPx * scatter;
			const amount = decayAmount(decay, glyph.timing.start, time) * glyph.drift.speed;
			x += glyph.drift.dx * heroPx * amount;
			y += glyph.drift.dy * heroPx * amount;
			rotation += glyph.drift.spin * amount * .18;
			const breath = heroPx * .022 * Math.min(1, decay.strength + .3);
			x += Math.sin(time * .9 + glyph.phase) * breath;
			y += Math.cos(time * 1.13 + glyph.phase * 1.7) * breath;
			return {
				x,
				y,
				rotation,
				gather,
				flying,
				scale: glyph.scale * pulse
			};
		};
		/**
		* 这一帧要画的行：当前行前后各 WINDOW_REACH 行，还在滑动（或径迹还没收拢）的换行再往前 WINDOW_REACH 行——
		* 槽位要看当前行 ±2 行的排版，更远的行透明度都是 0。只由时刻决定。
		*/
		const activeRange = (time) => {
			const { current } = resolveWindowCursor(options.lines, time);
			const first = firstLiveChange(time, current);
			return {
				current,
				low: Math.max(0, Math.min(current, first) - WINDOW_REACH),
				high: Math.min(lineCount - 1, Math.max(current, 0) + WINDOW_REACH)
			};
		};
		return {
			fontH,
			lineTransform,
			glyphLocal,
			activeRange
		};
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/text/windowProtection.ts
	const createWindowProtection = ({ lines, heroPx, lineOf }) => {
		const protectBoxes = [
			createProtectBox(),
			createProtectBox(),
			createProtectBox(),
			createProtectBox()
		];
		let protectCount = 0;
		/**
		* 这一帧的保护框：第 j 行作为当前行的权重 = 它这次换行的缓动进度 − 下一次换行的缓动进度（换行进度都是 t 的
		* 连续函数，所以权重也连续，求和为 1）。框是第 j 行此刻的墨迹框（横竖 × 单行 / 折行按当前的朝向与折行程度混合），
		* 跟着它的位置、缩放与转角。横竖转到一半时（字在飞、混合出来的框又宽又高，框边扫得很快）框渐隐，转完再回来。
		* 要先算好这一帧所有行的变换（frameTransforms）。
		*/
		const buildProtectBoxes = (time, current, low, frameTransforms) => {
			protectCount = 0;
			let later = 0;
			for (let j = current; j >= low && protectCount < protectBoxes.length; j -= 1) {
				const eased = easeInOutSine((time - (lines[j].startTime - LEAD)) / SLIDE);
				const transform = frameTransforms[j];
				const o = clamp01$2(transform.orient);
				const weight = (eased - later) * clamp01$2(transform.alpha) * (1 - 4 * o * (1 - o));
				later = eased;
				if (weight > 1e-4) {
					const [hSingle, hWrapped] = lineOf(j).flow[0];
					const [vSingle, vWrapped] = lineOf(j).flow[1];
					const w = transform.wrap;
					const inkW = lerp(lerp(hSingle.inkWidth, hWrapped.inkWidth, w), lerp(vSingle.inkWidth, vWrapped.inkWidth, w), transform.orient);
					const inkH = lerp(lerp(hSingle.inkHeight, hWrapped.inkHeight, w), lerp(vSingle.inkHeight, vWrapped.inkHeight, w), transform.orient);
					const box = protectBoxes[protectCount];
					box.line = j;
					box.x = transform.x;
					box.y = transform.y;
					box.cos = Math.cos(transform.rotation);
					box.sin = Math.sin(transform.rotation);
					box.halfW = inkW / 2 * transform.scale;
					box.halfH = inkH / 2 * transform.scale;
					box.margin = PROTECT_MARGIN * heroPx * transform.scale;
					box.weight = weight;
					protectCount += 1;
				}
				if (eased >= 1) break;
			}
		};
		return {
			build: buildProtectBoxes,
			/** 第 index 行一个字（画面坐标、半径）的保护系数：落进别的行的保护框就压暗，飞行途中按飞行强度不压。 */
			shield: (index, x, y, radius, flying) => protectCount > 0 ? protectedAlpha(protectionAt(protectBoxes, protectCount, index, x, y, radius) * (1 - flying)) : 1
		};
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/lumiere/lumiereUnitLayout.ts
/** 第 i 行成为当前行时用它所在镜头的排版（不在任何镜头里的行跟随前一个有歌词的镜头）。 */
	const lumiereTypographyOfLine = (shots) => (lineIndex) => {
		let found = shots[0];
		for (const shot of shots) {
			if (shot.lines.includes(lineIndex)) return shot.profile.typography;
			if (shot.lines.length > 0 && shot.lines[0] < lineIndex) found = shot;
		}
		return found.profile.typography;
	};

//#endregion
//#region .tools/folia-native-lumiere.ts
	var NumericNode = class {
		constructor() {
			this.children = [];
			this.anchor = { set() {} };
		}
		addChild(...c) {
			this.children.push(...c);
		}
	};
	function packProfile(p, seed, index, aspect) {
		const rig = p.light({
			aspect,
			random: createRng(seed + ":" + index + ":" + p.kind + ":light")
		}), art = p.lineArt({
			aspect,
			random: createRng(seed + ":" + index + ":" + p.kind + ":art")
		});
		const beams = rig.beams.map((b) => [
			[
				b.x,
				b.y,
				b.angle,
				b.width ?? 0
			],
			[
				b.spread,
				b.length,
				b.softness,
				b.intensity
			],
			[
				b.streaks,
				b.streakFreq,
				b.streakSpeed,
				b.core ?? .5
			],
			[...b.tint ?? [
				1,
				1,
				1
			], b.reveal ?? 0],
			[
				b.gobo ? {
					blinds: 1,
					cross: 2,
					lattice: 3,
					leaves: 4
				}[b.gobo.pattern] : 0,
				b.gobo?.frequency ?? 0,
				b.gobo?.duty ?? 1,
				b.gobo?.drift ?? 0
			],
			[
				b.sway?.amplitude ?? 0,
				b.sway?.period ?? 1,
				b.sway?.phase ?? 0,
				b.spectrum ?? 0
			],
			[
				b.pulse?.amplitude ?? 0,
				b.pulse?.period ?? 1,
				b.pulse?.phase ?? 0,
				b.reach ?? 0
			]
		]);
		const g = rig.fog, a = rig.glare, c = rig.caustic, w = rig.wave, m = p.motes;
		return {
			kind: p.kind,
			layout: [
				p.region.cx,
				p.region.cy,
				p.region.w,
				p.region.h,
				p.heroSize,
				p.camera.push,
				p.camera.driftX,
				p.camera.driftY,
				p.artGain ?? 1,
				p.decay.strength,
				p.decay.delay
			],
			beams,
			fogA: [
				g.density,
				g.tyndallBase,
				g.scale,
				g.warp
			],
			fogB: [
				g.driftX,
				g.driftY,
				g.ambient,
				5
			],
			glare: [
				a?.x ?? 0,
				a?.y ?? 0,
				a?.radius ?? 0,
				a?.intensity ?? 0
			],
			glareStreak: a?.streak ?? 0,
			caustic: [
				c?.scale ?? 0,
				c?.speed ?? 0,
				c?.inBeam ?? 0,
				c ? 1 : 0
			],
			floor: [
				c?.floor?.cx ?? 0,
				c?.floor?.cy ?? 0,
				c?.floor?.rx ?? 0,
				c?.floor?.ry ?? 0
			],
			floorStrength: c?.floor?.strength ?? 0,
			wave: [
				w ? {
					rings: 1,
					slits: 2,
					sources: 3,
					airy: 4
				}[w.mode] : 0,
				w?.frequency ?? 0,
				w?.speed ?? 0,
				w?.strength ?? 0
			],
			waveArea: [
				w?.cx ?? 0,
				w?.cy ?? 0,
				w?.radius ?? 0,
				w?.separation ?? 0
			],
			shield: w?.shield ?? .45,
			motes: [
				m.count,
				m.sizeMin,
				m.sizeMax,
				m.driftX,
				m.driftY,
				m.swirl,
				m.swirlPeriod,
				m.twinkle,
				m.ambient,
				m.gain
			],
			paths: art.paths,
			nodes: art.nodes,
			typography: p.typography,
			family: p.family,
			mood: p.mood
		};
	}
	function compileLumiere(json) {
		const input = JSON.parse(json);
		globalThis.nativeWidths = input.widths;
		globalThis.nativeLines = input.lines;
		const { width, height } = input, aspect = width / height;
		const unit = compileLumiereProgram(input.lines, input.seed, {}, { seamless: true }).paragraphs[0], seed = input.seed + ":" + unit.id;
		const shots = unit.shots.map((s, index) => ({
			...s,
			profile: packProfile(profileOf(s.kind), seed, index, aspect)
		}));
		const leadIndex = Math.max(0, shots.findIndex((s) => s.lineIndices.length));
		const lead = profileOf(shots[leadIndex].kind);
		const camera = {
			start: unit.startTime,
			end: unit.endTime,
			sections: unit.sections ?? [],
			push: lead.camera.push,
			driftX: lead.camera.driftX,
			driftY: lead.camera.driftY
		};
		const options = {
			width,
			height,
			lines: input.lines,
			font: input.fontFamily,
			weight: input.fontWeight,
			resolution: 1,
			region: {
				cx: lead.region.cx * aspect,
				cy: lead.region.cy,
				w: lead.region.w * aspect,
				h: lead.region.h
			},
			heroPx: lead.heroSize * height * input.fontScale,
			neighbors: 2,
			typography: lead.typography,
			typographyOf: lumiereTypographyOfLine(shots.map((s) => ({
				profile: profileOf(s.kind),
				lines: s.lineIndices
			}))),
			decay: lead.decay,
			seed,
			sprites: {
				dot: null,
				star: null
			},
			letterSpacing: .04
		};
		const band = frameBand(width, height), columnCap = (band.bottom - band.top) * height, maxWidthOf = (k) => (band.right - band.left) * height * (k === "crossed" ? .9 : 1);
		const context = {
			pixi: {
				Container: NumericNode,
				Sprite: NumericNode
			},
			font: options.font,
			weight: options.weight,
			resolution: 1,
			heroPx: options.heroPx,
			spacing: .04,
			seed,
			sprites: options.sprites,
			measurer: createTextMeasurer(options.font, options.weight, .04),
			limits: {
				horizontal: maxWidthOf("horizontal"),
				vertical: columnCap
			},
			keywords: void 0,
			driftSpeed: DRIFT_SPEED,
			placementsOf: (i) => freePlacements(createRng(seed + ":placements:" + i), options.region, .95)
		};
		const metas = buildLineMetas(input.lines), views = metas.map((meta, i) => buildLineView(context, i, meta, createRngAt(seed + ":window", meta.randomOffset))), lineOf = (i) => views[i];
		createWindowGeometry({
			options,
			lineCount: views.length,
			band,
			columnCap,
			maxWidthOf,
			nextAlpha: .95,
			lineOf
		});
		createWindowProtection({
			lines: input.lines,
			heroPx: options.heroPx,
			lineOf
		});
		const window = {
			region: options.region,
			heroPx: options.heroPx,
			decay: lead.decay,
			typography: input.lines.map((_, i) => options.typographyOf(i)),
			views: views.map((v) => ({
				flow: v.flow,
				placements: Array.from(v.placements),
				jitter: v.jitter,
				velocity: v.velocity,
				motionPhase: v.motionPhase,
				glyphs: v.glyphs.map((g) => ({
					flight: g.flight,
					scale: g.scale,
					vRotation: g.vRotation,
					starShape: g.starShape,
					drift: g.drift,
					scatter: g.scatter,
					phase: g.phase
				}))
			}))
		};
		const moteData = (spec, key) => {
			if (!spec) return null;
			const rng = createRng(key), items = [];
			for (let i = 0; i < spec.count * 2; i++) {
				const depth = rng(), bx = rng() * (aspect + .16) - .08, by = rng() * 1.16 - .08;
				items.push([
					bx,
					by,
					depth,
					spec.sizeMin + (spec.sizeMax - spec.sizeMin) * depth * depth,
					rng() * Math.PI * 2,
					rng() * Math.PI * 2,
					.6 + rng() * .8,
					.5 + rng() * .9,
					rng() * Math.PI * 2,
					.8 + rng() * 2.2
				]);
			}
			return {
				spec,
				items
			};
		};
		const motes = moteData(lead.motes, seed + ":motes"), front = moteData(lead.front, seed + ":front");
		const echoSize = lead.echo?.size ?? .34, echoes = [];
		const rng = createRng(seed + ":echo");
		input.lines.forEach((line, li) => segmentWords(line).forEach((word) => {
			if (word.blank) return;
			const end = Math.min(word.end, views[li].glyphs.length);
			if (end <= word.start) return;
			const item = {
				line: li,
				first: word.start,
				end,
				birth: views[li].glyphs[word.start].timing.start,
				across: (rng() - .5) * 2.8,
				speed: .7 + rng() * .7,
				scale: .4 + rng() * .6,
				rotation: (rng() - .5) * .9,
				stretch: .75 + rng() * .7,
				shear: (rng() - .5) * .12,
				spin: (rng() - .5) * .08,
				glyphs: []
			};
			for (let gi = word.start; gi < end; gi++) {
				const angle = rng() * Math.PI * 2;
				item.glyphs.push([
					Math.cos(angle),
					Math.sin(angle),
					.3 + rng() * .9,
					(rng() - .5) * .5
				]);
			}
			echoes.push(item);
		}));
		const baseSpec = lead.starfall, canOpen = unit.opening && unit.endTime - unit.startTime >= 2;
		const spec = baseSpec && canOpen ? {
			...baseSpec,
			opening: Math.min(baseSpec.opening, Math.max(.8, (unit.endTime - unit.startTime) * .4))
		} : baseSpec, stars = [], drops = [];
		if (spec) {
			const rng = createRng(seed + ":starfall");
			for (let i = 0; i < spec.stars; i++) {
				const bright = rng() < .08;
				stars.push([
					rng() * aspect,
					.02 + rng() ** 1.7 * .9,
					-.05 - rng() * .25,
					spec.opening * (.02 + .7 * rng() ** 1.4),
					.45 + rng() * .7,
					bright ? .018 + rng() * .02 : .003 + rng() * .006,
					bright ? 1 : 0,
					rng() * Math.PI * 2,
					.4 + rng() * 1.8
				]);
			}
			for (let i = 0; i < spec.rain; i++) {
				const spread = (rng() + rng() + rng() - 1.5) / 1.5;
				drops.push([
					aspect * (.5 + spread * spec.rainSpread * .5),
					1.25 / Math.max(spec.rainSpeed, .01) * (.7 + rng() * .6),
					rng(),
					.003 + rng() * .005,
					(rng() - .5) * .02
				]);
			}
		}
		const bursts = unit.shots.flatMap((shot, si) => {
			const spec = profileOf(shot.kind).burst;
			if (!spec) return [];
			const covered = new Set(shot.lineIndices);
			return planBursts(spec, input.lines.map((_, li) => ({ glyphs: covered.has(li) ? views[li].glyphs.flatMap((g, gi) => metas[li].graphemes[gi].trim() ? [{
				glyphIndex: gi,
				start: g.timing.start
			}] : []) : [] })), seed + ":" + si).map((t) => ({
				...t,
				size: t.size * spec.size,
				tint: spec.tint
			}));
		}).sort((a, b) => a.time - b.time);
		return JSON.stringify({
			bursts,
			opening: !!(canOpen && spec),
			shots,
			lead: leadIndex,
			camera,
			window,
			motes,
			front,
			echoSize,
			echoes,
			starfall: spec ? {
				spec,
				stars,
				drops
			} : null
		});
	}

//#endregion
//#region ../../web/folia-major/src/utils/lyrics/cjkSemanticLayout.ts
	const CJK_REGEX = /[\u4e00-\u9fa5\u3040-\u30ff\uac00-\ud7af]/;
	const WHITESPACE_REGEX = /^\s+$/;
	const APOSTROPHE_ONLY_REGEX = /^['’]\s*$/;
	const CONTRACTION_SUFFIX_REGEX = /^(s|t|m|d|ll|re|ve|em)\s*$/i;
	const DIRECT_CONTRACTION_REGEX = /^['’](s|t|m|d|ll|re|ve|em)\s*$/i;
	const TRAILING_APOSTROPHE_REGEX = /['’]\s*$/;
	const TRAILING_WORD_CHAR_REGEX = /[\p{L}\p{N}]$/u;
	const INLINE_CONTRACTION_REGEX = /[\p{L}\p{N}]+['’](s|t|m|d|ll|re|ve|em)/iu;
	const STICKY_TRAILING_PUNCTUATION_REGEX = /^[,.;:!?，。！？、：；）】》」』〉〕］)}\]"'’”’]+$/u;
	const hasCjkText = (text) => CJK_REGEX.test(text);
	const createSingleWordLayoutUnits = (words) => words.map((word) => ({
		text: word.text,
		words: [word],
		startTime: word.startTime,
		endTime: word.endTime,
		isSemantic: false
	}));
	const getWordSegments = (line) => {
		const segments = segmentLyricWords(line);
		return segments.length > 0 ? segments.map(({ segment, isWordLike }) => ({
			segment,
			isWordLike
		})) : null;
	};
	const appendWordsToUnit = (unit, text, words) => {
		unit.text += text;
		unit.words.push(...words);
		unit.endTime = words[words.length - 1]?.endTime ?? unit.endTime;
	};
	const cloneUnit = (unit) => ({
		...unit,
		words: [...unit.words]
	});
	const appendUnitToStickyUnit = (target, unit) => {
		target.text += unit.text;
		target.words.push(...unit.words);
		target.endTime = unit.endTime;
		target.isSticky = true;
	};
	const canAttachToPrevious = (text) => TRAILING_WORD_CHAR_REGEX.test(text.trimEnd());
	const endsWithApostrophe = (text) => TRAILING_APOSTROPHE_REGEX.test(text.trimEnd());
	const isApostropheOnlyUnit = (unit) => APOSTROPHE_ONLY_REGEX.test(unit.text.trim());
	const isContractionSuffixUnit = (unit) => CONTRACTION_SUFFIX_REGEX.test(unit.text.trim());
	const isDirectContractionUnit = (unit) => DIRECT_CONTRACTION_REGEX.test(unit.text.trim());
	const isStickyTrailingPunctuationUnit = (unit) => STICKY_TRAILING_PUNCTUATION_REGEX.test(unit.text.trim());
	const hasAttachedTrailingPunctuation = (unit) => {
		if (unit.words.length <= 1) return false;
		const lastWord = unit.words[unit.words.length - 1];
		return Boolean(lastWord && isStickyTrailingPunctuationUnit({
			text: lastWord.text,
			words: [lastWord],
			startTime: lastWord.startTime,
			endTime: lastWord.endTime,
			isSemantic: false
		}));
	};
	const hasInlineContraction = (unit) => unit.words.length > 1 && !unit.isSemantic && INLINE_CONTRACTION_REGEX.test(unit.text);
	const mapSegmentsToWords = (segments, words) => {
		const units = [];
		let wordIndex = 0;
		for (const segment of segments) {
			const segmentText = segment.segment;
			if (!segmentText || WHITESPACE_REGEX.test(segmentText)) continue;
			const startWordIndex = wordIndex;
			let collectedText = "";
			while (wordIndex < words.length && collectedText.length < segmentText.length) {
				collectedText += words[wordIndex].text;
				wordIndex += 1;
				if (!segmentText.startsWith(collectedText)) return null;
			}
			if (collectedText !== segmentText) return null;
			const segmentWords = words.slice(startWordIndex, wordIndex);
			const firstWord = segmentWords[0];
			const lastWord = segmentWords[segmentWords.length - 1];
			if (!firstWord || !lastWord) return null;
			if (!segment.isWordLike && units.length > 0) {
				appendWordsToUnit(units[units.length - 1], segmentText, segmentWords);
				continue;
			}
			units.push({
				text: segmentText,
				words: segmentWords,
				startTime: firstWord.startTime,
				endTime: lastWord.endTime,
				isSemantic: Boolean(segment.isWordLike && hasCjkText(segmentText) && segmentWords.length > 1)
			});
		}
		if (wordIndex !== words.length || units.length === 0) return null;
		return units;
	};
	const buildCjkSemanticLayoutUnits = (line) => {
		if (line.words.length === 0) return [];
		const fallbackUnits = createSingleWordLayoutUnits(line.words);
		if (!hasCjkText(line.fullText) && !hasWordSegmentationOverride(line)) return fallbackUnits;
		const segments = getWordSegments(line);
		if (!segments) return fallbackUnits;
		return mapSegmentsToWords(segments, line.words) ?? fallbackUnits;
	};
	const applyStickyPunctuationLayoutUnits = (units) => {
		const merged = [];
		for (let index = 0; index < units.length; index += 1) {
			const current = units[index];
			const previous = merged[merged.length - 1];
			if (!previous) {
				merged.push(cloneUnit(current));
				continue;
			}
			const next = units[index + 1];
			if (isApostropheOnlyUnit(current) && next && canAttachToPrevious(previous.text) && isContractionSuffixUnit(next)) {
				appendUnitToStickyUnit(previous, current);
				appendUnitToStickyUnit(previous, next);
				index += 1;
				continue;
			}
			if (isDirectContractionUnit(current) && canAttachToPrevious(previous.text)) {
				appendUnitToStickyUnit(previous, current);
				continue;
			}
			if (isContractionSuffixUnit(current) && endsWithApostrophe(previous.text)) {
				appendUnitToStickyUnit(previous, current);
				continue;
			}
			if (isStickyTrailingPunctuationUnit(current) && canAttachToPrevious(previous.text)) {
				appendUnitToStickyUnit(previous, current);
				continue;
			}
			merged.push(cloneUnit(current));
		}
		return merged.map((unit) => hasAttachedTrailingPunctuation(unit) || hasInlineContraction(unit) ? {
			...unit,
			isSticky: true
		} : unit);
	};
	const buildPostLyricLayoutUnits = (line, options = {}) => {
		const rawUnits = options.semantic ? buildCjkSemanticLayoutUnits(line) : createSingleWordLayoutUnits(line.words);
		return options.sticky ? applyStickyPunctuationLayoutUnits(rawUnits) : rawUnits;
	};
	const buildDisplayWordsFromLayoutUnits = (units) => units.flatMap((unit) => {
		if (!unit.isSticky || unit.isSemantic) return unit.words;
		return [{
			text: unit.text,
			startTime: unit.startTime,
			endTime: unit.endTime
		}];
	});

//#endregion
//#region ../../web/folia-major/src/components/visualizer/partita/partitaLayout.ts
	const PARTITA_LAYOUT_CACHE_LIMIT = 48;
	const graphemeSegmenter$1 = typeof Intl !== "undefined" ? new Intl.Segmenter(void 0, { granularity: "grapheme" }) : null;
	const splitGraphemes$1 = (text) => {
		if (!text) return [];
		if (graphemeSegmenter$1) return Array.from(graphemeSegmenter$1.segment(text), ({ segment }) => segment);
		return Array.from(text);
	};
	const clamp$7 = (value, min, max) => Math.max(min, Math.min(max, value));
	const clampPartitaStagger = (value, fallback) => Number.isFinite(value) ? clamp$7(value, 0, 180) : fallback;
	const resolvePartitaTuning = (tuning) => {
		const rawMin = clampPartitaStagger(tuning?.staggerMin ?? DEFAULT_PARTITA_TUNING.staggerMin, DEFAULT_PARTITA_TUNING.staggerMin);
		const rawMax = clampPartitaStagger(tuning?.staggerMax ?? DEFAULT_PARTITA_TUNING.staggerMax, DEFAULT_PARTITA_TUNING.staggerMax);
		return {
			showGuideLines: tuning?.showGuideLines ?? DEFAULT_PARTITA_TUNING.showGuideLines,
			useSemanticLayout: tuning?.useSemanticLayout ?? DEFAULT_PARTITA_TUNING.useSemanticLayout,
			staggerMin: Math.min(rawMin, rawMax),
			staggerMax: Math.max(rawMin, rawMax)
		};
	};
	const buildSequentialColumns = (line, theme, windowHeight, tuning) => {
		const intensity = theme.animationIntensity;
		const isChaotic = intensity === "chaotic";
		const isCalm = intensity === "calm";
		const totalGraphemes = Math.max(splitGraphemes$1(line.fullText.replace(/\s+/g, "")).length, line.words.length, 1);
		const layoutUnits = buildPostLyricLayoutUnits(line, {
			semantic: tuning.useSemanticLayout,
			sticky: true
		});
		const columns = [];
		let currentWords = [];
		const baseRowHeight = 100;
		const availableHeight = windowHeight * .65;
		const targetRowCount = Math.max(1, Math.floor(availableHeight / baseRowHeight));
		const actualRowCount = Math.min(layoutUnits.length, targetRowCount);
		let seed = line.startTime;
		const random = () => {
			const x = Math.sin(seed++) * 1e4;
			return x - Math.floor(x);
		};
		const chunks = [];
		let remainingUnits = layoutUnits.length;
		let remainingChunks = actualRowCount;
		let unitIndex = 0;
		for (let c = 0; c < actualRowCount; c++) {
			const isLastChunk = c === actualRowCount - 1;
			const avg = remainingUnits / remainingChunks;
			let chunkLength = 1;
			if (isLastChunk) chunkLength = remainingUnits;
			else {
				const max = Math.ceil(avg * 1.5);
				const min = 1;
				const randVal = random();
				chunkLength = Math.max(min, Math.min(max, Math.round(avg + (randVal - .5) * avg)));
			}
			chunkLength = Math.max(1, Math.min(chunkLength, remainingUnits - (remainingChunks - 1)));
			chunks.push(layoutUnits.slice(unitIndex, unitIndex + chunkLength));
			unitIndex += chunkLength;
			remainingUnits -= chunkLength;
			remainingChunks--;
		}
		chunks.forEach((chunkUnits, rowIndex) => {
			const chunkWords = chunkUnits.flatMap((unit) => unit.words);
			const displayWords = buildDisplayWordsFromLayoutUnits(chunkUnits);
			if (chunkWords.length === 0) return;
			const mergedTextForConfig = chunkWords.map((w) => w.text.trim()).join("");
			const graphemeCount = splitGraphemes$1(mergedTextForConfig.replace(/\s+/g, "")).length;
			const rowBias = rowIndex - (actualRowCount - 1) / 2;
			const isStaggeredLeft = rowIndex % 2 === 0;
			const staggerMagnitude = tuning.staggerMin + random() * Math.max(tuning.staggerMax - tuning.staggerMin, 0);
			const staggerX = isStaggeredLeft ? -staggerMagnitude : staggerMagnitude;
			const staggerScale = isCalm ? 1 : .8 + random() * .9;
			currentWords.push({
				chunkUnits,
				chunkWords,
				displayWords,
				order: rowIndex,
				rowIndex,
				config: {
					id: `${rowIndex}-${chunkWords[0].startTime}`,
					x: staggerX,
					y: isCalm ? 0 : rowBias * 2.5,
					rotate: isChaotic ? isStaggeredLeft ? -3 : 3 : 0,
					scale: (isChaotic ? 1 + Math.min(graphemeCount * .01, .05) : 1) * staggerScale,
					marginBottom: isChaotic ? "0.4rem" : "0.6rem",
					alignSelf: "center",
					passedRotate: (rowIndex % 2 === 0 ? 1 : -1) * (isChaotic ? 6 : 3)
				}
			});
		});
		if (currentWords.length > 0) columns.push({
			id: `column-${line.startTime}-0`,
			words: currentWords
		});
		return {
			columns,
			totalGraphemes,
			lineConfig: {
				perspective: theme.animationIntensity === "chaotic" ? 720 : 1e3,
				alignItems: "items-center",
				justifyContent: "justify-center",
				columnGap: "2rem"
			}
		};
	};
	const buildPartitaLayoutCacheKey = (line, theme, windowHeight, tuning) => {
		const windowHeightBucket = Math.round(windowHeight / 24);
		return [
			"semantic-layout-v1",
			line.startTime,
			line.endTime,
			line.words.length,
			line.fullText,
			getWordSegmentationKey(line),
			theme.animationIntensity,
			theme.fontWeight ?? "auto",
			windowHeightBucket,
			tuning.staggerMin,
			tuning.staggerMax,
			tuning.showGuideLines ? 1 : 0,
			tuning.useSemanticLayout ? 1 : 0
		].join("|");
	};
	const getOrBuildPartitaLayout = (cache, line, theme, windowHeight, tuning) => {
		const cacheKey = buildPartitaLayoutCacheKey(line, theme, windowHeight, tuning);
		const cached = cache.get(cacheKey);
		if (cached) return cached;
		const layout = buildSequentialColumns(line, theme, windowHeight, tuning);
		return rememberBounded(cache, cacheKey, layout, PARTITA_LAYOUT_CACHE_LIMIT);
	};

//#endregion
//#region .tools/folia-native-partita.ts
	function compilePartita(json) {
		const input = JSON.parse(json);
		globalThis.nativeWidths = input.widths;
		globalThis.nativeLines = input.lines;
		const theme = {
			fontWeight: input.fontWeight,
			animationIntensity: "normal"
		};
		return JSON.stringify(input.lines.map((line) => {
			const layout = getOrBuildPartitaLayout(/* @__PURE__ */ new Map(), line, theme, input.height, resolvePartitaTuning());
			return {
				graphemes: layout.totalGraphemes,
				chunks: layout.columns.flatMap((c) => c.words.map((chunk) => {
					const first = line.words.indexOf(chunk.chunkWords[0]), end = line.words.indexOf(chunk.chunkWords.at(-1)) + 1;
					let cursor = first;
					const words = chunk.displayWords.map((word) => {
						const a = cursor;
						let size = 0;
						while (cursor < end && size < word.text.length) size += line.words[cursor++].text.length;
						return {
							first: a,
							end: cursor,
							start: word.startTime,
							finish: word.endTime
						};
					});
					return {
						config: chunk.config,
						words
					};
				}))
			};
		}));
	}

//#endregion
//#region ../../web/folia-major/src/components/visualizer/pendolo/pendoloClockworkScene.ts
/** Builds theme-tinted stroke/fill colors for the current decor intensity. */
	const buildPendoloClockworkPalette = (primaryTextColor, accentTextColor, showGearDecor) => {
		const m = showGearDecor === "full" ? 1 : .6;
		return {
			decorOpacityMultiplier: m,
			primaryAlpha10: colorWithAlpha(primaryTextColor, .1 * m),
			primaryAlpha15: colorWithAlpha(primaryTextColor, .15 * m),
			accentAlpha35: colorWithAlpha(accentTextColor, .35 * m),
			gearPrimaryAlpha: colorWithAlpha(primaryTextColor, .42 * m),
			gearPrimarySubtleAlpha: colorWithAlpha(primaryTextColor, .32 * m),
			gearAccentAlpha: colorWithAlpha(accentTextColor, .58 * m),
			gearAccentStrongAlpha: colorWithAlpha(accentTextColor, .72 * m),
			planetGearAlpha: colorWithAlpha(primaryTextColor, .24 * m),
			jewelFillColor: colorWithAlpha(accentTextColor, .28 * m),
			jewelStrokeColor: colorWithAlpha(accentTextColor, .65 * m),
			mainGearFill: colorWithAlpha(accentTextColor, .08 * m),
			balanceGearFill: colorWithAlpha(accentTextColor, .1 * m),
			secondGearFill: colorWithAlpha(accentTextColor, .06 * m),
			bevelRing: colorWithAlpha(primaryTextColor, .18 * m),
			rivetStroke: colorWithAlpha(primaryTextColor, .25 * m),
			guillocheAlt: colorWithAlpha(primaryTextColor, .07 * m),
			genevaAlt: colorWithAlpha(primaryTextColor, .06 * m),
			idlerStroke: colorWithAlpha(primaryTextColor, .28 * m),
			idlerInner: colorWithAlpha(primaryTextColor, .18 * m)
		};
	};
	/** Shared layout anchors used by both Canvas2D and WebGL paths. */
	const resolvePendoloClockworkAnchors = (layout) => {
		const { centerX, centerY, baseRadius, lyricRingRadius } = layout;
		const balanceCx = centerX + baseRadius * .2;
		const balanceCy = centerY - baseRadius * .75;
		const balanceR = baseRadius * .28;
		const transCx = centerX + baseRadius * .32;
		const transCy = centerY + baseRadius * .78;
		const transR = baseRadius * .34;
		const secondGearCx = centerX + baseRadius * .68;
		const secondGearCy = centerY + baseRadius * .76;
		return {
			balanceCx,
			balanceCy,
			balanceR,
			transCx,
			transCy,
			transR,
			secondGearCx,
			secondGearCy,
			secondGearR: baseRadius * .16,
			idlerCx: (transCx + secondGearCx) * .5 + baseRadius * .04,
			idlerCy: (transCy + secondGearCy) * .5 - baseRadius * .02,
			idlerR: baseRadius * .08,
			focalAxisEndRadius: Math.min(lyricRingRadius - Math.max(14, baseRadius * .025)),
			orbitR: baseRadius * .52,
			planetR: baseRadius * .16,
			coverRadius: baseRadius * .88,
			gradientR: baseRadius * 1.65
		};
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/pendolo/pendoloClockworkMeshes.ts
/** x,y,rgba,partId */
	const PENDOLO_MESH_STRIDE = 7;
	const PENDOLO_MAX_PARTS = 32;
	/** Canvas2D's default miterLimit; sharper joins are bevelled, as Canvas2D does. */
	const PENDOLO_MITER_LIMIT = 10;
	var PendoloMeshBuilder = class {
		constructor() {
			this.data = [];
			this.activePartId = 0;
		}
		get vertexCount() {
			return this.data.length / 7;
		}
		toFloat32() {
			return new Float32Array(this.data);
		}
		setActivePartId(partId) {
			this.activePartId = partId;
		}
		pushVert(x, y, r, g, b, a) {
			this.data.push(x, y, r, g, b, a, this.activePartId);
		}
		pushTri(ax, ay, bx, by, cx, cy, r, g, b, a) {
			this.pushVert(ax, ay, r, g, b, a);
			this.pushVert(bx, by, r, g, b, a);
			this.pushVert(cx, cy, r, g, b, a);
		}
		appendLine(x0, y0, x1, y1, width, rgba, extend = 0) {
			const dx = x1 - x0;
			const dy = y1 - y0;
			const len = Math.hypot(dx, dy);
			if (len < 1e-6) return;
			const ux = dx / len;
			const uy = dy / len;
			const hw = width * .5;
			const nx = -uy * hw;
			const ny = ux * hw;
			const ax = x0 - ux * extend;
			const ay = y0 - uy * extend;
			const bx = x1 + ux * extend;
			const by = y1 + uy * extend;
			const [r, g, b, a] = rgba;
			this.pushTri(ax + nx, ay + ny, ax - nx, ay - ny, bx + nx, by + ny, r, g, b, a);
			this.pushTri(ax - nx, ay - ny, bx - nx, by - ny, bx + nx, by + ny, r, g, b, a);
		}
		/**
		* Strokes a polyline the way Canvas2D does by default: miter joins (falling back to a bevel
		* past a miter limit of 10) and butt caps. Neighbouring segments share their join vertices, so
		* a translucent stroke covers every pixel once; overlapping the segments instead blends the
		* joints twice and they show up as brighter dots.
		*/
		appendPolyline(points, width, rgba, closed = false) {
			const pts = points.filter((point, index) => {
				const previous = index > 0 ? points[index - 1] : null;
				return !previous || Math.hypot(point.x - previous.x, point.y - previous.y) > 1e-6;
			});
			if (closed && pts.length > 2) {
				const first = pts[0];
				const last = pts[pts.length - 1];
				if (Math.hypot(first.x - last.x, first.y - last.y) <= 1e-6) pts.pop();
			}
			const n = pts.length;
			if (n < 2) return;
			const hw = width * .5;
			const segmentCount = closed ? n : n - 1;
			const normals = [];
			for (let i = 0; i < segmentCount; i++) {
				const a = pts[i];
				const b = pts[(i + 1) % n];
				const len = Math.hypot(b.x - a.x, b.y - a.y);
				normals.push({
					x: -(b.y - a.y) / len,
					y: (b.x - a.x) / len
				});
			}
			const starts = [];
			const ends = [];
			const bevels = [];
			for (let i = 0; i < segmentCount; i++) {
				starts.push({
					x: normals[i].x * hw,
					y: normals[i].y * hw
				});
				ends.push({
					x: normals[i].x * hw,
					y: normals[i].y * hw
				});
			}
			const joinCount = closed ? n : n - 2;
			for (let j = 0; j < joinCount; j++) {
				const incoming = closed ? (j + segmentCount - 1) % segmentCount : j;
				const outgoing = closed ? j : j + 1;
				const vertex = pts[closed ? j : j + 1];
				const n1 = normals[incoming];
				const n2 = normals[outgoing];
				const mx = n1.x + n2.x;
				const my = n1.y + n2.y;
				const mLen = Math.hypot(mx, my);
				const cosHalf = mLen / 2;
				if (mLen > 1e-6 && 1 / cosHalf <= PENDOLO_MITER_LIMIT) {
					const scale = hw / cosHalf / mLen;
					const miter = {
						x: mx * scale,
						y: my * scale
					};
					ends[incoming] = miter;
					starts[outgoing] = miter;
				} else {
					const side = n1.x * n2.y - n1.y * n2.x > 0 ? -1 : 1;
					bevels.push({
						point: vertex,
						from: {
							x: n1.x * hw * side,
							y: n1.y * hw * side
						},
						to: {
							x: n2.x * hw * side,
							y: n2.y * hw * side
						}
					});
				}
			}
			const [r, g, b, a] = rgba;
			for (let i = 0; i < segmentCount; i++) {
				const p0 = pts[i];
				const p1 = pts[(i + 1) % n];
				const s0 = starts[i];
				const e0 = ends[i];
				this.pushTri(p0.x + s0.x, p0.y + s0.y, p0.x - s0.x, p0.y - s0.y, p1.x + e0.x, p1.y + e0.y, r, g, b, a);
				this.pushTri(p0.x - s0.x, p0.y - s0.y, p1.x - e0.x, p1.y - e0.y, p1.x + e0.x, p1.y + e0.y, r, g, b, a);
			}
			for (const bevel of bevels) {
				const { point, from, to } = bevel;
				this.pushTri(point.x, point.y, point.x + from.x, point.y + from.y, point.x + to.x, point.y + to.y, r, g, b, a);
			}
		}
		appendCircleStroke(cx, cy, radius, width, rgba, segments = 64) {
			if (radius <= 0 || width <= 0) return;
			const hw = width * .5;
			const inner = Math.max(0, radius - hw);
			const outer = radius + hw;
			const segs = Math.max(segments, Math.ceil(radius * 1.25));
			const [r, g, b, a] = rgba;
			for (let i = 0; i < segs; i++) {
				const a0 = i * Math.PI * 2 / segs;
				const a1 = (i + 1) * Math.PI * 2 / segs;
				const c0 = Math.cos(a0);
				const s0 = Math.sin(a0);
				const c1 = Math.cos(a1);
				const s1 = Math.sin(a1);
				this.pushTri(cx + outer * c0, cy + outer * s0, cx + inner * c0, cy + inner * s0, cx + outer * c1, cy + outer * s1, r, g, b, a);
				this.pushTri(cx + inner * c0, cy + inner * s0, cx + inner * c1, cy + inner * s1, cx + outer * c1, cy + outer * s1, r, g, b, a);
			}
		}
		appendFilledPolygonFromCenter(cx, cy, points, rgba) {
			if (points.length < 3) return;
			const [r, g, b, a] = rgba;
			for (let i = 0; i < points.length; i++) {
				const p0 = points[i];
				const p1 = points[(i + 1) % points.length];
				this.pushTri(cx, cy, p0.x, p0.y, p1.x, p1.y, r, g, b, a);
			}
		}
		appendArrowHead(tipX, tipY, rgba) {
			const [r, g, b, a] = rgba;
			this.pushTri(tipX, tipY - 4, tipX + 8, tipY, tipX, tipY + 4, r, g, b, a);
		}
	};
	/** Parses CSS color strings into RGBA 0..1. */
	const parseRgba = (color) => {
		const rgbaMatch = color.match(/rgba?\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*(?:,\s*([\d.]+)\s*)?\)/i);
		if (rgbaMatch) return [
			Number(rgbaMatch[1]) / 255,
			Number(rgbaMatch[2]) / 255,
			Number(rgbaMatch[3]) / 255,
			rgbaMatch[4] === void 0 ? 1 : Number(rgbaMatch[4])
		];
		const ch = parseColorChannels(color);
		if (!ch) return [
			1,
			1,
			1,
			1
		];
		return [
			ch.r / 255,
			ch.g / 255,
			ch.b / 255,
			1
		];
	};
	/** Cache key for layout + palette; motion angles are applied as uniforms. */
	const buildPendoloClockworkCacheKey = (input) => [
		input.centerX,
		input.centerY,
		Math.round(input.baseRadius * 10),
		Math.round(input.lyricRingRadius * 10),
		input.showGearDecor,
		input.primaryTextColor,
		input.accentTextColor
	].join("|");
	const gearTeethPointsLocal = (radius, teethCount, toothDepth) => {
		const innerR = radius - toothDepth;
		const outerR = radius;
		const anglePerTooth = Math.PI * 2 / teethCount;
		const pts = [];
		for (let i = 0; i < teethCount; i++) {
			const baseAngle = i * anglePerTooth;
			const angles = [
				baseAngle - anglePerTooth * .22,
				baseAngle - anglePerTooth * .12,
				baseAngle + anglePerTooth * .12,
				baseAngle + anglePerTooth * .22
			];
			const radii = [
				innerR,
				outerR,
				outerR,
				innerR
			];
			for (let k = 0; k < 4; k++) pts.push({
				x: radii[k] * Math.cos(angles[k]),
				y: radii[k] * Math.sin(angles[k])
			});
		}
		return pts;
	};
	const appendGearLocal = (mesh, radius, teethCount, toothDepth, stroke, lineWidth, fill) => {
		const pts = gearTeethPointsLocal(radius, teethCount, toothDepth);
		if (fill) mesh.appendFilledPolygonFromCenter(0, 0, pts, parseRgba(fill));
		mesh.appendPolyline(pts, lineWidth, parseRgba(stroke), true);
	};
	const appendSpokedWheelLocal = (mesh, hubR, rimR, spokeCount, stroke, lineWidth) => {
		const rgba = parseRgba(stroke);
		mesh.appendCircleStroke(0, 0, hubR, lineWidth, rgba);
		mesh.appendCircleStroke(0, 0, rimR, lineWidth, rgba);
		const angleStep = Math.PI * 2 / spokeCount;
		for (let i = 0; i < spokeCount; i++) {
			const a = i * angleStep;
			mesh.appendLine(hubR * Math.cos(a), hubR * Math.sin(a), rimR * Math.cos(a), rimR * Math.sin(a), lineWidth, rgba);
		}
		const midR = (hubR + rimR) * .5;
		const holeR = (rimR - hubR) * .22;
		for (let i = 0; i < spokeCount; i++) {
			const a = i * angleStep + angleStep * .5;
			mesh.appendCircleStroke(midR * Math.cos(a), midR * Math.sin(a), holeR, lineWidth, rgba, 24);
		}
	};
	const appendHairspringLocal = (mesh, startR, endR, coils, stroke, lineWidth) => {
		const totalAngle = coils * Math.PI * 2;
		const steps = Math.round(coils * 60);
		const pts = [];
		for (let i = 0; i <= steps; i++) {
			const t = i / steps;
			const angle = t * totalAngle;
			const r = startR + (endR - startR) * Math.pow(t, .9);
			pts.push({
				x: r * Math.cos(angle),
				y: r * Math.sin(angle)
			});
		}
		mesh.appendPolyline(pts, lineWidth, parseRgba(stroke), false);
	};
	const appendJewelLocal = (mesh, outerR, innerR, strokeOuter, strokeInner, outerWidth, innerWidth) => {
		mesh.appendCircleStroke(0, 0, outerR, outerWidth, parseRgba(strokeOuter), 28);
		mesh.appendCircleStroke(0, 0, innerR, innerWidth, parseRgba(strokeInner), 20);
	};
	const fixedOrigin = (x, y) => () => ({
		x,
		y
	});
	const fixedRotation = (angle) => () => angle;
	/**
	* Builds a rest-pose mesh scene. Same-transform pieces share a partId for one draw call.
	*/
	const buildPendoloClockworkCachedScene = (input) => {
		const key = buildPendoloClockworkCacheKey(input);
		const mesh = new PendoloMeshBuilder();
		const parts = [];
		const partKeys = /* @__PURE__ */ new Map();
		if (input.showGearDecor === "none") return {
			key,
			data: mesh.toFloat32(),
			parts,
			vertexCount: 0
		};
		const { centerX, centerY, baseRadius, primaryTextColor, accentTextColor, showGearDecor } = input;
		const anchors = resolvePendoloClockworkAnchors(input);
		const palette = buildPendoloClockworkPalette(primaryTextColor, accentTextColor, showGearDecor);
		const m = palette.decorOpacityMultiplier;
		const mainOrigin = fixedOrigin(centerX, centerY);
		const pushPart = (transformKey, build, getOrigin, getRotation) => {
			let partId = partKeys.get(transformKey);
			if (partId === void 0) {
				if (parts.length >= 32) {
					console.warn("[pendoloClockworkMeshes] exceeded PENDOLO_MAX_PARTS");
					return;
				}
				partId = parts.length;
				partKeys.set(transformKey, partId);
				parts.push({
					getOrigin,
					getRotation
				});
			}
			mesh.setActivePartId(partId);
			build();
		};
		pushPart("main-static", () => {
			const ringRgba = parseRgba(palette.primaryAlpha15);
			for (const r of [
				.3,
				.6,
				.85,
				1.15,
				1.4
			].map((f) => baseRadius * f)) mesh.appendCircleStroke(0, 0, r, 1, ringRgba);
			const bevel = parseRgba(palette.bevelRing);
			mesh.appendCircleStroke(0, 0, baseRadius * .88, .8, bevel);
			mesh.appendCircleStroke(0, 0, baseRadius * .92, .8, bevel);
			appendJewelLocal(mesh, 4.5, 2.5, palette.jewelStrokeColor, palette.jewelFillColor, 1.2, .8);
		}, mainOrigin, fixedRotation(0));
		pushPart("main-ticks", () => {
			const outerTickR = baseRadius * 1.15;
			for (let i = 0; i < 60; i++) {
				const angle = i * Math.PI * 2 / 60;
				const isMajor = i % 5 === 0;
				const tickLen = isMajor ? 12 : 6;
				mesh.appendLine(outerTickR * Math.cos(angle), outerTickR * Math.sin(angle), (outerTickR + tickLen) * Math.cos(angle), (outerTickR + tickLen) * Math.sin(angle), isMajor ? 1.5 : 1, parseRgba(isMajor ? palette.accentAlpha35 : palette.primaryAlpha15));
			}
		}, mainOrigin, (motion) => motion.escapementAngle * .2);
		pushPart("main-esc", () => {
			appendGearLocal(mesh, baseRadius + 8, 36, 10, palette.gearAccentAlpha, 2.2, palette.mainGearFill);
			appendSpokedWheelLocal(mesh, baseRadius * .2, baseRadius * .85, 6, palette.gearPrimaryAlpha, 1.8);
			const guillocheInnerR = baseRadius * .62;
			const guillocheOuterR = baseRadius * .83;
			for (let i = 0; i < 48; i++) {
				const a = i * Math.PI * 2 / 48;
				const innerOffset = i % 2 === 0 ? guillocheInnerR : guillocheInnerR + (guillocheOuterR - guillocheInnerR) * .3;
				mesh.appendLine(innerOffset * Math.cos(a), innerOffset * Math.sin(a), guillocheOuterR * Math.cos(a), guillocheOuterR * Math.sin(a), .7, parseRgba(i % 2 === 0 ? palette.primaryAlpha10 : palette.guillocheAlt));
			}
			const rivetR = baseRadius * .96;
			const rivet = parseRgba(palette.rivetStroke);
			for (let i = 0; i < 12; i++) {
				const a = i * Math.PI * 2 / 12;
				mesh.appendCircleStroke(rivetR * Math.cos(a), rivetR * Math.sin(a), 2.2, .8, rivet, 16);
			}
		}, mainOrigin, (motion) => motion.escapementAngle);
		pushPart("main-sun", () => appendGearLocal(mesh, baseRadius * .22, 12, 6, palette.gearAccentStrongAlpha, 2.1), mainOrigin, (motion) => -motion.escapementAngle * 2.5);
		for (let planetIdx = 0; planetIdx < 3; planetIdx++) {
			const idx = planetIdx;
			const planetOrigin = (motion) => {
				const planetAngle = motion.escapementAngle * .4 + idx * Math.PI * 2 / 3;
				return {
					x: centerX + anchors.orbitR * Math.cos(planetAngle),
					y: centerY + anchors.orbitR * Math.sin(planetAngle)
				};
			};
			pushPart(`planet-${idx}-gear`, () => appendGearLocal(mesh, anchors.planetR, 14, 5, palette.planetGearAlpha, 1.7), planetOrigin, (motion) => -motion.escapementAngle * 3 + idx * .5);
			pushPart(`planet-${idx}-jewel`, () => appendJewelLocal(mesh, 3.5, 1.8, palette.jewelStrokeColor, palette.jewelFillColor, 1, .7), planetOrigin, fixedRotation(0));
		}
		const balanceOrigin = fixedOrigin(anchors.balanceCx, anchors.balanceCy);
		pushPart("balance-gear", () => appendGearLocal(mesh, anchors.balanceR, 20, 7, palette.gearAccentStrongAlpha, 2.1, palette.balanceGearFill), balanceOrigin, (motion) => motion.phase * .1);
		pushPart("balance-static", () => {
			mesh.appendCircleStroke(0, 0, anchors.balanceR * .52, 1.8, parseRgba(palette.gearPrimarySubtleAlpha));
			appendJewelLocal(mesh, 3.5, 1.8, palette.jewelStrokeColor, palette.jewelFillColor, 1, .7);
		}, balanceOrigin, fixedRotation(0));
		pushPart("hairspring", () => appendHairspringLocal(mesh, anchors.balanceR * .56, anchors.balanceR * .88, 3.5, colorWithAlpha(accentTextColor, .3 * m), .8), balanceOrigin, (motion) => motion.bassOscillation * .6 + motion.phase * .1 * .3);
		const transOrigin = fixedOrigin(anchors.transCx, anchors.transCy);
		pushPart("trans-spin", () => {
			appendGearLocal(mesh, anchors.transR, 24, 7, palette.gearPrimaryAlpha, 1.8);
			appendSpokedWheelLocal(mesh, anchors.transR * .25, anchors.transR * .85, 5, palette.gearPrimarySubtleAlpha, 1.5);
			const genevaSpan = anchors.transR * 1.6;
			const genevaStep = genevaSpan / 10;
			const clipR = anchors.transR * .8;
			for (let i = 1; i <= 9; i++) {
				const yOff = -genevaSpan * .5 + i * genevaStep;
				const halfChord = Math.sqrt(Math.max(0, clipR * clipR - yOff * yOff));
				if (halfChord < .5) continue;
				mesh.appendLine(-halfChord, yOff, halfChord, yOff, .7, parseRgba(i % 2 === 0 ? palette.primaryAlpha10 : palette.genevaAlt));
			}
		}, transOrigin, (motion) => -motion.escapementAngle * 1.4);
		pushPart("trans-jewel", () => appendJewelLocal(mesh, 3.5, 1.8, palette.jewelStrokeColor, palette.jewelFillColor, 1, .7), transOrigin, fixedRotation(0));
		const secondOrigin = fixedOrigin(anchors.secondGearCx, anchors.secondGearCy);
		pushPart("second-spin", () => appendGearLocal(mesh, anchors.secondGearR, 15, 5, palette.gearAccentAlpha, 1.8, palette.secondGearFill), secondOrigin, (motion) => motion.secondGearAngle);
		pushPart("second-spoke", () => appendSpokedWheelLocal(mesh, anchors.secondGearR * .28, anchors.secondGearR * .76, 4, palette.gearPrimarySubtleAlpha, 1.3), secondOrigin, (motion) => -motion.secondGearAngle);
		pushPart("second-jewel", () => appendJewelLocal(mesh, 2.8, 1.5, palette.jewelStrokeColor, palette.jewelFillColor, .8, .6), secondOrigin, fixedRotation(0));
		const idlerOrigin = fixedOrigin(anchors.idlerCx, anchors.idlerCy);
		pushPart("idler-spin", () => appendGearLocal(mesh, anchors.idlerR, 10, 3.5, palette.idlerStroke, 1.3), idlerOrigin, (motion) => motion.escapementAngle * 2.2);
		pushPart("idler-static", () => {
			mesh.appendCircleStroke(0, 0, anchors.idlerR * .45, .8, parseRgba(palette.idlerInner));
			appendJewelLocal(mesh, 2.2, 1.2, palette.jewelStrokeColor, palette.jewelFillColor, .7, .5);
		}, idlerOrigin, fixedRotation(0));
		pushPart("axis", () => {
			const axis = parseRgba(palette.gearAccentStrongAlpha);
			mesh.appendLine(centerX + baseRadius * .8, centerY, centerX + anchors.focalAxisEndRadius, centerY, 2, axis);
			mesh.appendArrowHead(centerX + anchors.focalAxisEndRadius, centerY, axis);
		}, fixedOrigin(0, 0), fixedRotation(0));
		return {
			key,
			data: mesh.toFloat32(),
			parts,
			vertexCount: mesh.vertexCount
		};
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/fume/fumeMath.ts
	const graphemeSegmenter = typeof Intl !== "undefined" ? new Intl.Segmenter(void 0, { granularity: "grapheme" }) : null;
	const splitGraphemes = (text) => {
		if (!text) return [];
		if (graphemeSegmenter) return Array.from(graphemeSegmenter.segment(text), ({ segment }) => segment);
		return Array.from(text);
	};
	const clamp$6 = (value, min, max) => Math.max(min, Math.min(max, value));
	const mix$1 = (from, to, amount) => from + (to - from) * amount;
	const easeInOutCubic = (value) => {
		const normalized = clamp$6(value, 0, 1);
		return normalized < .5 ? 4 * normalized * normalized * normalized : 1 - Math.pow(-2 * normalized + 2, 3) / 2;
	};
	const hashString$1 = (input) => {
		let hash = 2166136261;
		for (let index = 0; index < input.length; index += 1) {
			hash ^= input.charCodeAt(index);
			hash = Math.imul(hash, 16777619);
		}
		return hash >>> 0;
	};
	const seeded$1 = (seed) => {
		return hashString$1(seed) % 1e4 / 1e4;
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/fume/fumeReveal.ts
	const buildWordRangesFromWords = (line, graphemes) => {
		if (line.words.length === 0 || graphemes.length === 0) return [];
		const rangedWords = line.words.filter((word) => splitGraphemes(word.text).length > 0);
		if (rangedWords.length === 0) return [];
		const ranges = [];
		let cursor = 0;
		for (let wordIndex = 0; wordIndex < rangedWords.length; wordIndex += 1) {
			const word = rangedWords[wordIndex];
			const wordGraphemes = splitGraphemes(word.text);
			const start = clamp$6(cursor, 0, graphemes.length);
			let end = clamp$6(start + wordGraphemes.length, start, graphemes.length);
			while (end < graphemes.length && /\s/.test(graphemes[end] ?? "")) end += 1;
			ranges.push({
				wordIndex,
				word,
				start,
				end,
				colorStart: start,
				colorEnd: end,
				graphemeTimings: buildWordGraphemeTimings(word)
			});
			cursor = end;
		}
		return ranges;
	};
	const resolveWordRevealProgress = (range, currentTimeValue) => {
		if (range.word.endTime <= range.word.startTime) return currentTimeValue >= range.word.endTime ? 1 : 0;
		const duration = Math.max(range.word.endTime - range.word.startTime, .08);
		return clamp$6((currentTimeValue - range.word.startTime) / duration, 0, 1);
	};
	const resolvePrintedGlyphsInRange = (range, currentTimeValue) => {
		const length = Math.max(range.end - range.start, 0);
		if (length === 0) return 0;
		if (currentTimeValue < range.word.startTime) return 0;
		const timedGlyphCount = range.word.syllables?.length ? Math.min(range.graphemeTimings.length, length) : 0;
		if (timedGlyphCount > 0) {
			if (currentTimeValue >= range.word.endTime) return length;
			let printed = 0;
			for (let index = 0; index < timedGlyphCount; index += 1) if (currentTimeValue >= range.graphemeTimings[index].startTime) printed = index + 1;
			return clamp$6(printed, 0, length);
		}
		const progress = resolveWordRevealProgress(range, currentTimeValue);
		if (progress >= 1) return length;
		return clamp$6(Math.floor(progress * length + .2), progress > 0 ? 1 : 0, length);
	};
	const hasRevealCompletedByLineEnd = (line, currentTimeValue) => currentTimeValue >= line.endTime;
	const resolvePrintedGraphemeCount = (line, wordRanges, graphemeCount, currentTimeValue) => {
		if (graphemeCount === 0) return 0;
		if (currentTimeValue < line.startTime) return 0;
		if (hasRevealCompletedByLineEnd(line, currentTimeValue)) return graphemeCount;
		if (wordRanges.length === 0) {
			const duration = Math.max(line.endTime - line.startTime, .12);
			const progress = clamp$6((currentTimeValue - line.startTime) / duration, 0, 1);
			return clamp$6(Math.floor(progress * graphemeCount + (progress > 0 ? 1 : 0)), 0, graphemeCount);
		}
		let printed = 0;
		for (let index = 0; index < wordRanges.length; index += 1) {
			const range = wordRanges[index];
			const partial = resolvePrintedGlyphsInRange(range, currentTimeValue);
			printed = range.start + partial;
			if (partial < range.end - range.start) return clamp$6(printed, 0, graphemeCount);
		}
		return clamp$6(printed, 0, graphemeCount);
	};
	const resolvePrintedGraphemeProgress = (line, wordRanges, graphemeCount, currentTimeValue) => {
		if (graphemeCount === 0) return 0;
		if (currentTimeValue < line.startTime) return 0;
		if (hasRevealCompletedByLineEnd(line, currentTimeValue)) return graphemeCount;
		if (wordRanges.length === 0) {
			const duration = Math.max(line.endTime - line.startTime, .12);
			const progress = clamp$6((currentTimeValue - line.startTime) / duration, 0, 1);
			return clamp$6(progress * graphemeCount, 0, graphemeCount);
		}
		let printed = 0;
		for (let index = 0; index < wordRanges.length; index += 1) {
			const range = wordRanges[index];
			if (currentTimeValue < range.word.startTime) return clamp$6(printed, 0, graphemeCount);
			const progress = resolveWordRevealProgress(range, currentTimeValue);
			const length = Math.max(range.end - range.start, 0);
			printed = range.start + progress * length;
			if (progress < 1) return clamp$6(printed, 0, graphemeCount);
		}
		return clamp$6(printed, 0, graphemeCount);
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/fume/fumeTextMeasure.ts
	const FUME_PRETEXT_OPTIONS = { whiteSpace: "pre-wrap" };
	const buildSegmentMetas = (prepared) => {
		const segmentMetas = [];
		const graphemes = [];
		let graphemeCursor = 0;
		for (const segment of prepared.segments) {
			const segmentGraphemes = splitGraphemes(segment);
			segmentMetas.push({
				graphemeStart: graphemeCursor,
				graphemeEnd: graphemeCursor + segmentGraphemes.length,
				graphemeCount: segmentGraphemes.length
			});
			graphemes.push(...segmentGraphemes);
			graphemeCursor += segmentGraphemes.length;
		}
		return {
			graphemes,
			segmentMetas
		};
	};
	const cursorToGlobalOffset = (cursor, segmentMetas) => {
		if (segmentMetas.length === 0) return 0;
		const segment = segmentMetas[cursor.segmentIndex];
		if (!segment) return segmentMetas[segmentMetas.length - 1].graphemeEnd;
		return clamp$6(segment.graphemeStart + cursor.graphemeIndex, segment.graphemeStart, segment.graphemeEnd);
	};
	const getPartialSegmentWidth = (prepared, segmentIndex, segmentMeta, startOffset, endOffset) => {
		const localStart = clamp$6(startOffset - segmentMeta.graphemeStart, 0, segmentMeta.graphemeCount);
		const localEnd = clamp$6(endOffset - segmentMeta.graphemeStart, 0, segmentMeta.graphemeCount);
		if (localEnd <= localStart) return 0;
		if (localStart === 0 && localEnd === segmentMeta.graphemeCount) return prepared.widths[segmentIndex] ?? 0;
		const breakableFitAdvances = prepared.breakableFitAdvances[segmentIndex];
		if (breakableFitAdvances && breakableFitAdvances.length > 0) {
			let width = 0;
			for (let index = localStart; index < localEnd; index += 1) width += breakableFitAdvances[index] ?? 0;
			return width;
		}
		const fullWidth = prepared.widths[segmentIndex] ?? 0;
		if (segmentMeta.graphemeCount === 0) return fullWidth;
		return fullWidth * ((localEnd - localStart) / segmentMeta.graphemeCount);
	};
	const widthBetweenOffsets = (prepared, segmentMetas, startOffset, endOffset) => {
		if (endOffset <= startOffset) return 0;
		let width = 0;
		for (let segmentIndex = 0; segmentIndex < segmentMetas.length; segmentIndex += 1) {
			const meta = segmentMetas[segmentIndex];
			if (endOffset <= meta.graphemeStart) break;
			if (startOffset >= meta.graphemeEnd) continue;
			const sliceStart = Math.max(startOffset, meta.graphemeStart);
			const sliceEnd = Math.min(endOffset, meta.graphemeEnd);
			width += getPartialSegmentWidth(prepared, segmentIndex, meta, sliceStart, sliceEnd);
		}
		return width;
	};
	const buildGlyphOffsets = (prepared, segmentMetas, startOffset, graphemeCount) => {
		const offsets = new Array(graphemeCount);
		for (let index = 0; index < graphemeCount; index += 1) offsets[index] = widthBetweenOffsets(prepared, segmentMetas, startOffset, startOffset + index);
		return offsets;
	};
	const resolveGlyphAdvance = (renderLine, graphemeIndex) => {
		const currentOffset = renderLine.glyphOffsets[graphemeIndex] ?? 0;
		const nextOffset = graphemeIndex < renderLine.graphemes.length - 1 ? renderLine.glyphOffsets[graphemeIndex + 1] ?? renderLine.width : renderLine.width;
		return Math.max(nextOffset - currentOffset, 0);
	};
	const buildRenderSegments = (prepared, segmentMetas, lineStart, lineEnd, fontSpec) => {
		const segments = [];
		for (let segmentIndex = 0; segmentIndex < segmentMetas.length; segmentIndex += 1) {
			const meta = segmentMetas[segmentIndex];
			if (lineEnd <= meta.graphemeStart) break;
			if (lineStart >= meta.graphemeEnd) continue;
			const start = Math.max(lineStart, meta.graphemeStart);
			const end = Math.min(lineEnd, meta.graphemeEnd);
			if (end <= start) continue;
			const localStart = start - lineStart;
			const localEnd = end - lineStart;
			const segmentText = prepared.segments[segmentIndex] ?? "";
			const segmentGraphemes = splitGraphemes(segmentText);
			const text = start === meta.graphemeStart && end === meta.graphemeEnd ? segmentText : segmentGraphemes.slice(start - meta.graphemeStart, end - meta.graphemeStart).join("");
			const measuredGlyphOffsets = measureSegmentGlyphOffsets(text, fontSpec);
			segments.push({
				text,
				start,
				end,
				localStart,
				localEnd,
				x: widthBetweenOffsets(prepared, segmentMetas, lineStart, start),
				width: widthBetweenOffsets(prepared, segmentMetas, start, end),
				isFullSegment: start === meta.graphemeStart && end === meta.graphemeEnd,
				measuredGlyphOffsets
			});
		}
		return segments;
	};
	const buildFontSpec = (fontPx, variant, fontFamily, theme) => {
		return `${resolveThemeFontWeight(theme, variant === "hero" ? 780 : 640)} ${fontPx}px ${fontFamily}`;
	};
	let segmentMeasureCanvas = null;
	const SEGMENT_MEASURE_CACHE_LIMIT = 2048;
	const segmentMeasureCache = /* @__PURE__ */ new Map();
	const measureSegmentGlyphOffsets = (text, fontSpec) => {
		const cacheKey = `${fontSpec}__${text}`;
		const cached = segmentMeasureCache.get(cacheKey);
		if (cached) return cached;
		const graphemes = splitGraphemes(text);
		const offsets = new Array(graphemes.length + 1).fill(0);
		if (typeof document === "undefined") return offsets;
		if (!segmentMeasureCanvas) segmentMeasureCanvas = document.createElement("canvas");
		const context = segmentMeasureCanvas.getContext("2d");
		if (!context) return offsets;
		context.font = fontSpec;
		const measured = measurePrefixOffsets(graphemes, (prefix) => context.measureText(prefix).width);
		return rememberBounded(segmentMeasureCache, cacheKey, measured, SEGMENT_MEASURE_CACHE_LIMIT);
	};
	const buildWordRangeIndexByOffset = (graphemeCount, wordRanges, rangeKind = "timing") => {
		const indices = new Array(graphemeCount).fill(-1);
		for (let rangeIndex = 0; rangeIndex < wordRanges.length; rangeIndex += 1) {
			const range = wordRanges[rangeIndex];
			const start = rangeKind === "color" ? range.colorStart : range.start;
			const end = rangeKind === "color" ? range.colorEnd : range.end;
			for (let offset = start; offset < end && offset < graphemeCount; offset += 1) indices[offset] = rangeIndex;
		}
		return indices;
	};
	const countRenderableGraphemes = (text) => splitGraphemes(text).filter((value) => value.trim().length > 0).length;
	const buildPreparedSingleLine = (text, fontFamily, width, variant, lyricsFontScale, densityScale, heroScale, theme) => {
		let low = variant === "hero" ? 18 : 10;
		let high = variant === "hero" ? 58 : 30;
		let best = null;
		for (let iteration = 0; iteration < 8; iteration += 1) {
			const candidateFontPx = (low + high) / 2 * lyricsFontScale * densityScale * (variant === "hero" ? heroScale : 1);
			const fontSpec = buildFontSpec(candidateFontPx, variant, fontFamily, theme);
			const prepared = prepareWithSegments(text, fontSpec, FUME_PRETEXT_OPTIONS);
			const layout = layoutWithLines(prepared, width, Math.round(candidateFontPx * (variant === "hero" ? 1.02 : 1.06)));
			if (layout.lineCount <= 1) {
				best = {
					fontPx: candidateFontPx,
					prepared,
					layout
				};
				low = (low + high) / 2;
			} else high = (low + high) / 2;
		}
		if (best) return best;
		const fallbackFontPx = (variant === "hero" ? 18 : 10) * lyricsFontScale * densityScale * (variant === "hero" ? heroScale : 1);
		const fontSpec = buildFontSpec(fallbackFontPx, variant, fontFamily, theme);
		const prepared = prepareWithSegments(text, fontSpec, FUME_PRETEXT_OPTIONS);
		return {
			fontPx: fallbackFontPx,
			prepared,
			layout: layoutWithLines(prepared, width, Math.round(fallbackFontPx * (variant === "hero" ? 1.02 : 1.06)))
		};
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/fume/fumeArticleLayout.ts
	const nowMs = () => typeof performance !== "undefined" ? performance.now() : Date.now();
	const createFumeLayoutTiming = () => ({
		lines: 0,
		prepareLayoutMs: 0,
		placementMs: 0,
		renderDetailsMs: 0
	});
	const chooseNaturalBlockVariant = (line, index, total) => {
		const graphemeCount = countRenderableGraphemes(line.fullText);
		if (graphemeCount === 0) return "body";
		if (line.isChorus && graphemeCount <= 22) return "hero";
		const shortEnough = graphemeCount >= 4 && graphemeCount <= 28;
		const centered = Math.abs(index - total / 2) / Math.max(total, 1);
		const random = seeded$1(`${line.fullText}:${index}`);
		return shortEnough && centered < .72 && ((index + 1) % 6 === 0 || random > .965) ? "hero" : "body";
	};
	const chooseFallbackHeroBlockIndex = (entries) => {
		if (entries.length === 0) return -1;
		if (entries.some(({ line }, blockIndex) => chooseNaturalBlockVariant(line, blockIndex, entries.length) === "hero")) return -1;
		let bestIndex = -1;
		let bestScore = Number.NEGATIVE_INFINITY;
		entries.forEach(({ line }, blockIndex) => {
			const graphemeCount = countRenderableGraphemes(line.fullText);
			if (graphemeCount === 0) return;
			if (!(graphemeCount >= 4 && graphemeCount <= 28) && !(graphemeCount <= 36)) return;
			const centerScore = 1 - Math.abs(blockIndex - entries.length / 2) / Math.max(entries.length, 1);
			const lengthScore = graphemeCount >= 6 && graphemeCount <= 22 ? 1 : graphemeCount <= 28 ? .72 : .36;
			const chorusScore = line.isChorus ? .28 : 0;
			const stableJitter = seeded$1(`${line.fullText}:${blockIndex}:fallback-hero`) * .04;
			const score = centerScore * .62 + lengthScore * .34 + chorusScore + stableJitter;
			if (score > bestScore) {
				bestScore = score;
				bestIndex = blockIndex;
			}
		});
		if (bestIndex >= 0) return bestIndex;
		let shortestIndex = -1;
		let shortestCount = Number.POSITIVE_INFINITY;
		entries.forEach(({ line }, blockIndex) => {
			const graphemeCount = countRenderableGraphemes(line.fullText);
			if (graphemeCount > 0 && graphemeCount < shortestCount) {
				shortestCount = graphemeCount;
				shortestIndex = blockIndex;
			}
		});
		return shortestIndex;
	};
	const chooseBlockVariant = (line, index, total, forcedHeroIndex) => index === forcedHeroIndex ? "hero" : chooseNaturalBlockVariant(line, index, total);
	function buildArticleLayoutAttempt(lines, viewport, layoutTheme, lyricsFontScale, fumeTuning, options) {
		if (viewport.width <= 0 || viewport.height <= 0 || lines.length === 0) return null;
		const { paperWidth, viewportHeight, columns, gap, densityScale, seedKey, mode = "render", timing } = options;
		const shouldBuildRenderDetails = mode === "render";
		const horizontalMargin = Math.max(viewport.width * .86, 280);
		const verticalMargin = Math.max(viewport.height * .82, 220);
		const columnWidth = (paperWidth - gap * (columns - 1)) / columns;
		const fontFamily = resolveThemeFontStack(layoutTheme);
		const filteredLines = lines.map((line, index) => ({
			line,
			index
		})).filter((entry) => entry.line.fullText.trim().length > 0).sort((left, right) => {
			return seeded$1(`${seedKey}:${left.index}:${left.line.fullText}`) - seeded$1(`${seedKey}:${right.index}:${right.line.fullText}`);
		});
		const blocks = [];
		const columnHeights = Array.from({ length: columns }, () => verticalMargin);
		let bodyColumnTieCursor = 0;
		let heroPlacementTieCursor = 0;
		const forcedHeroIndex = chooseFallbackHeroBlockIndex(filteredLines);
		filteredLines.forEach(({ line, index }, blockIndex) => {
			timing && (timing.lines += 1);
			const variant = chooseBlockVariant(line, blockIndex, filteredLines.length, forcedHeroIndex);
			const heroSpanColumns = variant === "hero" ? Math.min(columns, columns <= 1 ? 1 : 2) : 1;
			const heroSpanWidth = heroSpanColumns > 1 ? columnWidth * heroSpanColumns + gap * (heroSpanColumns - 1) : paperWidth;
			const blockWidth = variant === "hero" ? heroSpanColumns === 1 ? paperWidth : columns === 2 ? columnWidth * 1.5 + gap * .5 : heroSpanWidth : columnWidth;
			const paddingY = 0;
			const innerWidth = Math.max(blockWidth - 0, 120);
			const prepareLayoutStart = timing ? nowMs() : 0;
			const preparedSingleLine = buildPreparedSingleLine(line.fullText, fontFamily, innerWidth, variant, lyricsFontScale, densityScale, fumeTuning.heroScale, layoutTheme);
			if (timing) timing.prepareLayoutMs += nowMs() - prepareLayoutStart;
			const fontPx = preparedSingleLine.fontPx;
			const lineHeight = Math.round(fontPx * (variant === "hero" ? 1.02 : 1.06));
			const layout = preparedSingleLine.layout;
			const blockGap = variant === "hero" ? Math.max(Math.round(lineHeight * .2), 6) : Math.max(Math.round(lineHeight * .08), 2);
			const blockHeight = 0 + layout.lines.length * lineHeight;
			let x = 0;
			let y = 0;
			const placementStart = timing ? nowMs() : 0;
			if (variant === "hero") {
				if (heroSpanColumns === 1) {
					y = Math.max(...columnHeights);
					x = horizontalMargin;
					columnHeights[0] = y + blockHeight + blockGap;
				} else {
					let bestHeight = Number.POSITIVE_INFINITY;
					let candidateStarts = [];
					for (let startColumn = 0; startColumn <= columns - heroSpanColumns; startColumn += 1) {
						let coveredHeight = 0;
						for (let columnIndex = startColumn; columnIndex < startColumn + heroSpanColumns; columnIndex += 1) coveredHeight = Math.max(coveredHeight, columnHeights[columnIndex] ?? 0);
						if (coveredHeight < bestHeight) {
							bestHeight = coveredHeight;
							candidateStarts = [startColumn];
						} else if (coveredHeight === bestHeight) candidateStarts.push(startColumn);
					}
					const targetStart = candidateStarts.length > 0 ? candidateStarts[heroPlacementTieCursor % candidateStarts.length] : 0;
					heroPlacementTieCursor += 1;
					y = bestHeight;
					x = horizontalMargin + targetStart * (columnWidth + gap) + Math.max((heroSpanWidth - blockWidth) * .5, 0);
					for (let columnIndex = targetStart; columnIndex < targetStart + heroSpanColumns; columnIndex += 1) columnHeights[columnIndex] = y + blockHeight + blockGap;
				}
			} else {
				let targetColumn = 0;
				let minHeight = columnHeights[0] ?? 0;
				const candidateColumns = [0];
				for (let columnIndex = 1; columnIndex < columns; columnIndex += 1) {
					const height = columnHeights[columnIndex] ?? 0;
					if (height < minHeight) {
						minHeight = height;
						candidateColumns.length = 0;
						candidateColumns.push(columnIndex);
					} else if (height === minHeight) candidateColumns.push(columnIndex);
				}
				targetColumn = candidateColumns[bodyColumnTieCursor % candidateColumns.length] ?? 0;
				bodyColumnTieCursor += 1;
				x = horizontalMargin + targetColumn * (columnWidth + gap);
				y = columnHeights[targetColumn];
				columnHeights[targetColumn] = y + blockHeight + blockGap;
			}
			if (timing) timing.placementMs += nowMs() - placementStart;
			if (shouldBuildRenderDetails) {
				const renderDetailsStart = timing ? nowMs() : 0;
				const prepared = preparedSingleLine.prepared;
				const fontSpec = buildFontSpec(fontPx, variant, fontFamily, layoutTheme);
				const { graphemes, segmentMetas } = buildSegmentMetas(prepared);
				const wordRanges = buildWordRangesFromWords(line, graphemes);
				const wordRangeIndexByOffset = buildWordRangeIndexByOffset(graphemes.length, wordRanges);
				const colorRangeIndexByOffset = buildWordRangeIndexByOffset(graphemes.length, wordRanges, "color");
				const renderLines = layout.lines.map((layoutLine, lineIndex) => {
					const start = cursorToGlobalOffset(layoutLine.start, segmentMetas);
					const end = cursorToGlobalOffset(layoutLine.end, segmentMetas);
					const lineGraphemes = splitGraphemes(layoutLine.text);
					return {
						id: `${line.startTime}-${lineIndex}`,
						text: layoutLine.text,
						start,
						end,
						graphemes: lineGraphemes,
						glyphOffsets: buildGlyphOffsets(prepared, segmentMetas, start, lineGraphemes.length),
						segments: buildRenderSegments(prepared, segmentMetas, start, end, fontSpec),
						left: variant === "hero" ? Math.max((blockWidth - layoutLine.width) * .08, 0) : 0,
						top: paddingY + lineIndex * lineHeight,
						width: layoutLine.width
					};
				});
				blocks.push({
					id: `fume-${line.startTime}-${index}`,
					sourceLineIndex: index,
					line,
					variant,
					x,
					y,
					width: blockWidth,
					height: blockHeight,
					innerWidth,
					fontPx,
					lineHeight,
					prepared,
					layout,
					graphemes,
					segmentMetas,
					wordRanges,
					wordRangeIndexByOffset,
					colorRangeIndexByOffset,
					renderLines
				});
				if (timing) timing.renderDetailsMs += nowMs() - renderDetailsStart;
			}
		});
		const articleHeight = Math.max(0, ...columnHeights) + verticalMargin;
		const metrics = {
			width: paperWidth + horizontalMargin * 2,
			height: articleHeight,
			viewportHeight,
			columns,
			gap,
			paperBounds: {
				left: horizontalMargin,
				top: verticalMargin,
				right: horizontalMargin + paperWidth,
				bottom: Math.max(articleHeight - verticalMargin, verticalMargin)
			}
		};
		if (!shouldBuildRenderDetails) return metrics;
		const chronologicalBlocks = [...blocks].sort((left, right) => left.sourceLineIndex - right.sourceLineIndex);
		const blockBySourceLineIndex = /* @__PURE__ */ new Map();
		for (const block of chronologicalBlocks) blockBySourceLineIndex.set(block.sourceLineIndex, block);
		const firstRenderableStartTime = chronologicalBlocks[0]?.line.startTime ?? Number.POSITIVE_INFINITY;
		const lastChronologicalRenderEndTime = chronologicalBlocks.length > 0 ? getLineRenderEndTime(chronologicalBlocks[chronologicalBlocks.length - 1].line) : Number.NEGATIVE_INFINITY;
		return {
			...metrics,
			blocks,
			blockBySourceLineIndex,
			chronologicalBlocks,
			firstRenderableStartTime,
			lastChronologicalRenderEndTime
		};
	}
	const buildArticleLayout = (lines, viewport, layoutTheme, lyricsFontScale, fumeTuning) => {
		if (viewport.width <= 0 || viewport.height <= 0 || lines.length === 0) return null;
		const paperWidth = clamp$6(Math.max(viewport.width * 1.95, viewport.width + 520), 920, 2400);
		const viewportHeight = Math.max(viewport.height, 240);
		const maxColumns = paperWidth >= 1120 ? 4 : paperWidth >= 760 ? 3 : paperWidth >= 500 ? 2 : 1;
		const targetHeight = viewportHeight * 2.45;
		const layoutSeedKey = layoutTheme.name;
		let bestOptions = null;
		let bestScore = Number.POSITIVE_INFINITY;
		const totalStart = nowMs();
		const measureTiming = createFumeLayoutTiming();
		const renderTiming = createFumeLayoutTiming();
		const measureColumnTimings = /* @__PURE__ */ new Map();
		let measureAttemptCount = 0;
		for (let columns = maxColumns; columns >= 1; columns -= 1) {
			let low = .82;
			let high = 1.42;
			const gap = clamp$6(Math.round(paperWidth * (columns >= 4 ? .0065 : columns === 3 ? .0085 : .0115)), 6, 14);
			const columnTiming = createFumeLayoutTiming();
			measureColumnTimings.set(columns, columnTiming);
			for (let iteration = 0; iteration < 8; iteration += 1) {
				const densityScale = (low + high) / 2;
				measureAttemptCount += 1;
				const layout = buildArticleLayoutAttempt(lines, viewport, layoutTheme, lyricsFontScale, fumeTuning, {
					paperWidth,
					viewportHeight,
					columns,
					gap,
					densityScale,
					seedKey: `${layoutSeedKey}:${columns}:${paperWidth}`,
					mode: "measure",
					timing: columnTiming
				});
				if (!layout) continue;
				const score = Math.abs(layout.height - targetHeight) + (layout.height < targetHeight ? 0 : (layout.height - targetHeight) * .14);
				if (score < bestScore) {
					bestScore = score;
					layout.height;
					bestOptions = {
						paperWidth,
						viewportHeight,
						columns,
						gap,
						densityScale,
						seedKey: `${layoutSeedKey}:${columns}:${paperWidth}`,
						mode: "render",
						timing: renderTiming
					};
				}
				if (layout.height < targetHeight) low = densityScale;
				else high = densityScale;
			}
			measureTiming.lines += columnTiming.lines;
			measureTiming.prepareLayoutMs += columnTiming.prepareLayoutMs;
			measureTiming.placementMs += columnTiming.placementMs;
			measureTiming.renderDetailsMs += columnTiming.renderDetailsMs;
		}
		const renderStart = nowMs();
		const article = bestOptions ? buildArticleLayoutAttempt(lines, viewport, layoutTheme, lyricsFontScale, fumeTuning, bestOptions) : null;
		nowMs() - renderStart;
		nowMs() - totalStart;
		return article;
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/FumeBackground.ts
	const clamp$5 = (value, min, max) => Math.max(min, Math.min(max, value));
	const mix = (from, to, amount) => from + (to - from) * amount;
	const hashString = (input) => {
		let hash = 2166136261;
		for (let index = 0; index < input.length; index += 1) {
			hash ^= input.charCodeAt(index);
			hash = Math.imul(hash, 16777619);
		}
		return hash >>> 0;
	};
	const seeded = (seed) => {
		return hashString(seed) % 1e4 / 1e4;
	};
	const normalizeBounds = (bounds, worldWidth, worldHeight) => ({
		left: clamp$5(Math.min(bounds.left, bounds.right), 0, worldWidth),
		top: clamp$5(Math.min(bounds.top, bounds.bottom), 0, worldHeight),
		right: clamp$5(Math.max(bounds.left, bounds.right), 0, worldWidth),
		bottom: clamp$5(Math.max(bounds.top, bounds.bottom), 0, worldHeight)
	});
	const choosePaperHaloAnchor = ({ paperBounds, worldWidth, worldHeight, width, height, seedKey }) => {
		if (seeded(`${seedKey}:inside-chance`) < .22) return {
			x: clamp$5(mix(paperBounds.left + width * .12, paperBounds.right - width * .12, seeded(`${seedKey}:inside-x`)), 0, worldWidth),
			y: clamp$5(mix(paperBounds.top + height * .12, paperBounds.bottom - height * .12, seeded(`${seedKey}:inside-y`)), 0, worldHeight)
		};
		const side = Math.floor(seeded(`${seedKey}:side`) * 4) % 4;
		const overflowX = width * mix(.16, .24, seeded(`${seedKey}:overflow-x`));
		const overflowY = height * mix(.16, .24, seeded(`${seedKey}:overflow-y`));
		const spanJitterX = width * mix(-.18, .18, seeded(`${seedKey}:span-jitter-x`));
		const spanJitterY = height * mix(-.18, .18, seeded(`${seedKey}:span-jitter-y`));
		if (side === 0) return {
			x: clamp$5(paperBounds.left - overflowX, 0, worldWidth),
			y: clamp$5(mix(paperBounds.top - height * .12, paperBounds.bottom + height * .12, seeded(`${seedKey}:y`)) + spanJitterY, 0, worldHeight)
		};
		if (side === 1) return {
			x: clamp$5(paperBounds.right + overflowX, 0, worldWidth),
			y: clamp$5(mix(paperBounds.top - height * .12, paperBounds.bottom + height * .12, seeded(`${seedKey}:y`)) + spanJitterY, 0, worldHeight)
		};
		if (side === 2) return {
			x: clamp$5(mix(paperBounds.left - width * .12, paperBounds.right + width * .12, seeded(`${seedKey}:x`)) + spanJitterX, 0, worldWidth),
			y: clamp$5(paperBounds.top - overflowY, 0, worldHeight)
		};
		return {
			x: clamp$5(mix(paperBounds.left - width * .12, paperBounds.right + width * .12, seeded(`${seedKey}:x`)) + spanJitterX, 0, worldWidth),
			y: clamp$5(paperBounds.bottom + overflowY, 0, worldHeight)
		};
	};
	const buildFumeBackgroundScene = ({ viewport, world, paperBounds, seed }) => {
		if (viewport.width <= 0 || viewport.height <= 0 || world.width <= 0 || world.height <= 0) return {
			width: Math.max(world.width, viewport.width, 1),
			height: Math.max(world.height, viewport.height, 1),
			shapes: []
		};
		const worldWidth = Math.max(world.width, viewport.width * 1.2);
		const worldHeight = Math.max(world.height, viewport.height * 1.2);
		const baseUnit = clamp$5(Math.min(viewport.width, viewport.height) * .72, 320, 760);
		const defaultPaperBounds = {
			left: worldWidth * .24,
			top: worldHeight * .18,
			right: worldWidth * .76,
			bottom: worldHeight * .82
		};
		const resolvedPaperBounds = normalizeBounds(paperBounds ?? defaultPaperBounds, worldWidth, worldHeight);
		const shapeKinds = [
			"ring",
			"square",
			"cross",
			"ring",
			"square",
			"cross"
		];
		const shapeCount = worldWidth > worldHeight ? 8 : 7;
		const sparkBands = [
			"treble",
			"vocal",
			"mid",
			"treble",
			"lowMid"
		];
		const sparkCount = worldWidth > worldHeight ? 12 : 9;
		const sparkAreaLeft = worldWidth * .2;
		const sparkAreaRight = worldWidth * .8;
		const sparkAreaTop = worldHeight * .18;
		const sparkAreaBottom = worldHeight * .82;
		const sparkAreaWidth = sparkAreaRight - sparkAreaLeft;
		const sparkAreaHeight = sparkAreaBottom - sparkAreaTop;
		const sparkColumns = Math.ceil(Math.sqrt(sparkCount * (worldWidth / Math.max(worldHeight, 1))));
		const sparkRows = Math.ceil(sparkCount / sparkColumns);
		const sparkCellWidth = sparkAreaWidth / sparkColumns;
		const sparkCellHeight = sparkAreaHeight / sparkRows;
		const baseShapes = Array.from({ length: shapeCount }).map((_, index) => {
			const localSeed = `${seed ?? "fume"}:${worldWidth}:${worldHeight}:${index}`;
			const kind = shapeKinds[index % shapeKinds.length];
			const width = baseUnit * mix(.82, 1.36, seeded(`${localSeed}:size`));
			const height = width;
			const anchor = choosePaperHaloAnchor({
				paperBounds: resolvedPaperBounds,
				worldWidth,
				worldHeight,
				width,
				height,
				seedKey: localSeed
			});
			return {
				id: `fume-bg-${index}`,
				kind,
				x: anchor.x,
				y: anchor.y,
				width,
				height,
				rotation: mix(-Math.PI * .2, Math.PI * .2, seeded(`${localSeed}:rotation`)),
				rotationSpeed: mix(-.045, .045, seeded(`${localSeed}:rotation-speed`)),
				strokeWidth: mix(.25, 2.1, seeded(`${localSeed}:stroke-width`)),
				opacity: mix(.01, .16, seeded(`${localSeed}:opacity`)),
				color: seeded(`${localSeed}:color`) > .5 ? "accent" : "secondary",
				depth: seeded(`${localSeed}:depth`),
				ringGapStart: kind === "ring" ? mix(-Math.PI, Math.PI, seeded(`${localSeed}:gap-start`)) : void 0,
				ringGapSize: kind === "ring" ? mix(Math.PI * .12, Math.PI * .24, seeded(`${localSeed}:gap-size`)) : void 0
			};
		});
		const sparkShapes = Array.from({ length: sparkCount }).map((_, index) => {
			const localSeed = `${seed ?? "fume"}:${worldWidth}:${worldHeight}:spark:${index}`;
			const width = baseUnit * mix(.1, .24, seeded(`${localSeed}:size`));
			const column = index % sparkColumns;
			const row = Math.floor(index / sparkColumns);
			const jitterX = mix(-.32, .32, seeded(`${localSeed}:jitter-x`)) * sparkCellWidth;
			const jitterY = mix(-.32, .32, seeded(`${localSeed}:jitter-y`)) * sparkCellHeight;
			const x = clamp$5(sparkAreaLeft + (column + .5) * sparkCellWidth + jitterX, sparkAreaLeft, sparkAreaRight);
			const y = clamp$5(sparkAreaTop + (row + .5) * sparkCellHeight + jitterY, sparkAreaTop, sparkAreaBottom);
			return {
				id: `fume-spark-${index}`,
				kind: "spark",
				x,
				y,
				width,
				height: width,
				rotation: mix(-Math.PI, Math.PI, seeded(`${localSeed}:rotation`)),
				rotationSpeed: mix(-.18, .18, seeded(`${localSeed}:rotation-speed`)),
				strokeWidth: mix(.75, 1.7, seeded(`${localSeed}:stroke-width`)),
				opacity: mix(.08, .22, seeded(`${localSeed}:opacity`)),
				color: seeded(`${localSeed}:color`) > .5 ? "accent" : "secondary",
				depth: seeded(`${localSeed}:depth`),
				audioBand: sparkBands[index % sparkBands.length]
			};
		});
		return {
			width: worldWidth,
			height: worldHeight,
			shapes: [...baseShapes, ...sparkShapes].sort((left, right) => left.depth - right.depth)
		};
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/fume/fumeCamera.ts
	const resolveSmoothBlockFocusPoint = (block, printedProgress) => {
		if (block.renderLines.length === 0) return {
			x: block.x + block.width * .5,
			y: block.y + block.height * .5
		};
		const effectiveOffset = clamp$6(printedProgress, 0, block.graphemes.length);
		const findRenderLineIndex = (offset) => {
			const exactIndex = block.renderLines.findIndex((renderLine) => offset <= renderLine.end);
			return exactIndex >= 0 ? exactIndex : block.renderLines.length - 1;
		};
		const resolvePointOnRenderLine = (lineIndex, offset) => {
			const targetLine = block.renderLines[lineIndex] ?? block.renderLines[block.renderLines.length - 1];
			const clampedOffset = clamp$6(offset, targetLine.start, targetLine.end);
			const baseOffset = Math.floor(clampedOffset);
			const fractionalOffset = clampedOffset - baseOffset;
			const baseWidth = widthBetweenOffsets(block.prepared, block.segmentMetas, targetLine.start, baseOffset);
			const localGlyphIndex = baseOffset - targetLine.start;
			const glyphAdvance = localGlyphIndex >= 0 && localGlyphIndex < targetLine.graphemes.length ? resolveGlyphAdvance(targetLine, localGlyphIndex) : 0;
			const minX = block.x + targetLine.left;
			const maxX = minX + targetLine.width;
			return {
				x: clamp$6(minX + baseWidth + glyphAdvance * fractionalOffset, minX, maxX),
				y: block.y + targetLine.top + block.lineHeight * .5
			};
		};
		const targetLineIndex = findRenderLineIndex(effectiveOffset);
		let point = resolvePointOnRenderLine(targetLineIndex, effectiveOffset);
		const currentLine = block.renderLines[targetLineIndex];
		const crossLineBlendWindow = .7;
		if (targetLineIndex > 0 && effectiveOffset < currentLine.start + crossLineBlendWindow) {
			const previousLine = block.renderLines[targetLineIndex - 1];
			const blend = easeInOutCubic(clamp$6(1 - (effectiveOffset - previousLine.end) / crossLineBlendWindow, 0, 1));
			const previousPoint = resolvePointOnRenderLine(targetLineIndex - 1, previousLine.end);
			point = {
				x: mix$1(point.x, previousPoint.x, blend),
				y: mix$1(point.y, previousPoint.y, blend)
			};
		} else if (targetLineIndex < block.renderLines.length - 1 && effectiveOffset > currentLine.end - crossLineBlendWindow) {
			const nextLine = block.renderLines[targetLineIndex + 1];
			const blend = easeInOutCubic(clamp$6((effectiveOffset - (currentLine.end - crossLineBlendWindow)) / crossLineBlendWindow, 0, 1));
			const nextPoint = resolvePointOnRenderLine(targetLineIndex + 1, nextLine.start);
			point = {
				x: mix$1(point.x, nextPoint.x, blend),
				y: mix$1(point.y, nextPoint.y, blend)
			};
		}
		return point;
	};
	const resolveCameraScaleForBlock = (block, viewport) => {
		const minViewportSide = Math.max(Math.min(viewport.width, viewport.height), 1);
		const targetLineHeight = clamp$6(minViewportSide * .115, 64, 124);
		return clamp$6(targetLineHeight / Math.max(block.lineHeight, 1), .88, 2.2);
	};
	const resolveFocusBlock = (article, currentLineIndex, currentTimeValue) => {
		if (currentLineIndex >= 0) {
			const active = article.blockBySourceLineIndex.get(currentLineIndex) ?? null;
			if (active) return active;
		}
		const chronologicalLastBlock = article.chronologicalBlocks[article.chronologicalBlocks.length - 1] ?? null;
		if (chronologicalLastBlock && currentTimeValue >= article.lastChronologicalRenderEndTime) return chronologicalLastBlock;
		for (let index = article.chronologicalBlocks.length - 1; index >= 0; index -= 1) {
			const block = article.chronologicalBlocks[index];
			if (resolvePrintedGraphemeCount(block.line, block.wordRanges, block.graphemes.length, currentTimeValue) > 0) return block;
		}
		return article.chronologicalBlocks[0] ?? null;
	};

//#endregion
//#region ../../web/folia-major/node_modules/earcut/src/earcut.js
/**
	* A vertex in a circular doubly linked list representing a polygon ring.
	* `prev`/`next` are always linked (set immediately after {@link createNode}), so they're typed
	* non-null; `prevZ`/`nextZ` are the z-order list links and are null at the ends.
	*
	* @typedef {object} Node
	* @property {number} i vertex index in the coordinates array
	* @property {number} x vertex x coordinate
	* @property {number} y vertex y coordinate
	* @property {Node} prev previous vertex node in the polygon ring
	* @property {Node} next next vertex node in the polygon ring
	* @property {number} z z-order curve value; doubles as the owning block index during eliminateHoles
	* @property {Node | null} prevZ previous node in z-order
	* @property {Node | null} nextZ next node in z-order
	*/
	/** @type {Set<Node>} */
	const steiners = /* @__PURE__ */ new Set();
	let filteredOut = false;
	/**
	* Triangulate a polygon given as a flat array of vertex coordinates.
	*
	* @param {ArrayLike<number>} data flat array of vertex coordinates
	* @param {ArrayLike<number> | null} [holeIndices] indices (in vertices, not coordinates) where each hole ring starts
	* @param {number} [dim=2] number of coordinates per vertex in `data`
	* @returns {number[]} triangles as triplets of vertex indices into `data`
	* @example earcut([10,0, 0,50, 60,60, 70,10]); // [1,0,3, 3,2,1]
	*/
	function earcut(data, holeIndices, dim = 2) {
		const hasHoles = holeIndices && holeIndices.length;
		const outerLen = hasHoles ? holeIndices[0] * dim : data.length;
		if (steiners.size) steiners.clear();
		let outerNode = linkedList(data, 0, outerLen, dim, true);
		/** @type {number[]} */
		const triangles = [];
		if (!outerNode || outerNode.next === outerNode.prev) return triangles;
		let minX = 0, minY = 0, invSize = 0;
		if (hasHoles) outerNode = eliminateHoles(data, holeIndices, outerNode, dim);
		if (data.length > 80 * dim) {
			minX = data[0];
			minY = data[1];
			let maxX = minX;
			let maxY = minY;
			for (let i = dim; i < outerLen; i += dim) {
				const x = data[i];
				const y = data[i + 1];
				if (x < minX) minX = x;
				if (y < minY) minY = y;
				if (x > maxX) maxX = x;
				if (y > maxY) maxY = y;
			}
			invSize = Math.max(maxX - minX, maxY - minY);
			invSize = invSize !== 0 ? 32767 / invSize : 0;
		}
		earcutLinked(outerNode, triangles, minX, minY, invSize);
		return triangles;
	}
	/** @param {ArrayLike<number>} data @param {number} start @param {number} end @param {number} dim @param {boolean} clockwise @returns {Node | null} */
	function linkedList(data, start, end, dim, clockwise) {
		/** @type {Node | null} */
		let last = null;
		if (clockwise === signedArea(data, start, end, dim) > 0) for (let i = start; i < end; i += dim) last = insertNode(i / dim | 0, data[i], data[i + 1], last);
		else for (let i = end - dim; i >= start; i -= dim) last = insertNode(i / dim | 0, data[i], data[i + 1], last);
		if (last && equals(last, last.next)) {
			removeNode(last);
			last = last.next;
		}
		return last;
	}
	/** @param {Node} start @param {Node} [end] @returns {Node} */
	function filterPoints(start, end = start) {
		const full = end === start;
		let p = start, again;
		do {
			again = false;
			if (p !== p.next && (steiners.size === 0 || !steiners.has(p)) && (equals(p, p.next) || area(p.prev, p, p.next) === 0)) {
				if (full || p === end) end = p.prev;
				filteredOut = true;
				removeNode(p);
				p = p.prev;
				again = true;
			} else if (full || p !== end) {
				p = p.next;
				again = !full;
			}
		} while (again || p !== end);
		return end;
	}
	/** @param {Node} ear @param {number[]} triangles @param {number} minX @param {number} minY @param {number} invSize */
	function earcutLinked(ear, triangles, minX, minY, invSize) {
		if (invSize) indexCurve(ear, minX, minY, invSize);
		let stop = ear, cured = false;
		while (ear.prev !== ear.next) {
			const prev = ear.prev;
			/** @type {Node} */
			const next = ear.next;
			if (area(prev, ear, next) < 0 && (invSize ? isEarHashed(ear, minX, minY, invSize) : isEar(ear))) {
				triangles.push(prev.i, ear.i, next.i);
				removeNode(ear);
				ear = next;
				stop = next;
				continue;
			}
			ear = next;
			if (ear === stop) {
				filteredOut = false;
				ear = filterPoints(ear);
				if (filteredOut) {
					stop = ear;
					continue;
				}
				if (!cured) {
					ear = cureLocalIntersections(ear, triangles);
					stop = ear;
					cured = true;
					continue;
				}
				splitEarcut(ear, triangles, minX, minY, invSize);
				break;
			}
		}
	}
	/** @param {Node} ear @returns {boolean} */
	function isEar(ear) {
		const a = ear.prev, b = ear, c = ear.next, ax = a.x, bx = b.x, cx = c.x, ay = a.y, by = b.y, cy = c.y, x0 = Math.min(ax, bx, cx), y0 = Math.min(ay, by, cy), x1 = Math.max(ax, bx, cx), y1 = Math.max(ay, by, cy);
		let p = c.next;
		while (p !== a) {
			if (p.x >= x0 && p.x <= x1 && p.y >= y0 && p.y <= y1 && !(ax === p.x && ay === p.y) && pointInTriangle(ax, ay, bx, by, cx, cy, p.x, p.y) && area(p.prev, p, p.next) >= 0) return false;
			p = p.next;
		}
		return true;
	}
	/** @param {Node} ear @param {number} minX @param {number} minY @param {number} invSize @returns {boolean} */
	function isEarHashed(ear, minX, minY, invSize) {
		const a = ear.prev, b = ear, c = ear.next, ax = a.x, bx = b.x, cx = c.x, ay = a.y, by = b.y, cy = c.y, x0 = Math.min(ax, bx, cx), y0 = Math.min(ay, by, cy), x1 = Math.max(ax, bx, cx), y1 = Math.max(ay, by, cy), minZ = zOrder(x0, y0, minX, minY, invSize), maxZ = zOrder(x1, y1, minX, minY, invSize);
		let p = ear.prevZ;
		while (p && p.z >= minZ) {
			if (p.x >= x0 && p.x <= x1 && p.y >= y0 && p.y <= y1 && p !== c && !(ax === p.x && ay === p.y) && pointInTriangle(ax, ay, bx, by, cx, cy, p.x, p.y) && area(p.prev, p, p.next) >= 0) return false;
			p = p.prevZ;
		}
		let n = ear.nextZ;
		while (n && n.z <= maxZ) {
			if (n.x >= x0 && n.x <= x1 && n.y >= y0 && n.y <= y1 && n !== c && !(ax === n.x && ay === n.y) && pointInTriangle(ax, ay, bx, by, cx, cy, n.x, n.y) && area(n.prev, n, n.next) >= 0) return false;
			n = n.nextZ;
		}
		return true;
	}
	/** @param {Node} start @param {number[]} triangles @returns {Node} */
	function cureLocalIntersections(start, triangles) {
		let p = start;
		let cured = false;
		do {
			const a = p.prev, b = p.next.next;
			if (intersects(a, p, p.next, b, false) && locallyInside(a, b) && locallyInside(b, a)) {
				triangles.push(a.i, p.i, b.i);
				removeNode(p);
				removeNode(p.next);
				p = start = b;
				cured = true;
			}
			p = p.next;
		} while (p !== start);
		return cured ? filterPoints(p) : p;
	}
	/** @param {Node} start @param {number[]} triangles @param {number} minX @param {number} minY @param {number} invSize */
	function splitEarcut(start, triangles, minX, minY, invSize) {
		let a = start;
		do {
			let b = a.next.next;
			while (b !== a.prev) {
				if (a.i !== b.i && isValidDiagonal(a, b)) {
					let c = splitPolygon(a, b);
					a = filterPoints(a, a.next);
					c = filterPoints(c, c.next);
					earcutLinked(a, triangles, minX, minY, invSize);
					earcutLinked(c, triangles, minX, minY, invSize);
					return;
				}
				b = b.next;
			}
			a = a.next;
		} while (a !== start);
	}
	let indexActive = false;
	/** @param {ArrayLike<number>} data @param {ArrayLike<number>} holeIndices @param {Node} outerNode @param {number} dim @returns {Node} */
	function eliminateHoles(data, holeIndices, outerNode, dim) {
		const queue = [];
		for (let i = 0, len = holeIndices.length; i < len; i++) {
			const list = linkedList(data, holeIndices[i] * dim, i < len - 1 ? holeIndices[i + 1] * dim : data.length, dim, false);
			if (list === list.next) steiners.add(list);
			queue.push(getLeftmost(list));
		}
		queue.sort(compareXYSlope);
		buildBlockIndex(data.length / dim, holeIndices.length);
		indexSegment(outerNode, outerNode);
		indexActive = true;
		for (let i = 0; i < queue.length; i++) outerNode = eliminateHole(queue[i], outerNode);
		indexActive = false;
		return filterPoints(outerNode);
	}
	/** @param {Node} a @param {Node} b @returns {number} */
	function compareXYSlope(a, b) {
		return a.x - b.x || a.y - b.y || (a.next.y - a.y) / (a.next.x - a.x) - (b.next.y - b.y) / (b.next.x - b.x);
	}
	/** @param {Node} hole @param {Node} outerNode @returns {Node} */
	function eliminateHole(hole, outerNode) {
		const bridge = findHoleBridge(hole, outerNode);
		if (!bridge) return outerNode;
		const bridgeReverse = splitPolygon(bridge, hole);
		const bridge2 = bridgeReverse.next;
		indexSegment(bridge, bridge2.next);
		filterPoints(bridgeReverse, bridgeReverse.next);
		return filterPoints(bridge, bridge.next);
	}
	const K = 16;
	let blockBBox = /* @__PURE__ */ new Float64Array(0);
	let numBlocks = 0;
	/** @type {Node[]} */
	const blockHead = [];
	/** @type {Node[]} */
	const blockStop = [];
	/** @param {number} maxNodes @param {number} numHoles */
	function buildBlockIndex(maxNodes, numHoles) {
		const maxBlocks = Math.ceil((maxNodes + 2 * numHoles) / K) + numHoles + 2;
		if (blockBBox.length < maxBlocks * 4) blockBBox = new Float64Array(maxBlocks * 4);
		numBlocks = 0;
	}
	/** @param {Node} head @param {Node} stop */
	function indexSegment(head, stop) {
		let p = head;
		do {
			const b = numBlocks++;
			blockHead[b] = p;
			let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
			let k = 0;
			do {
				const c = p.next;
				p.z = b;
				if (p.x < minX) minX = p.x;
				if (p.x > maxX) maxX = p.x;
				if (p.y < minY) minY = p.y;
				if (p.y > maxY) maxY = p.y;
				if (c.x < minX) minX = c.x;
				if (c.x > maxX) maxX = c.x;
				if (c.y < minY) minY = c.y;
				if (c.y > maxY) maxY = c.y;
				p = c;
			} while (++k < K && p !== stop);
			blockStop[b] = p;
			const g = b * 4;
			blockBBox[g] = minX;
			blockBBox[g + 1] = minY;
			blockBBox[g + 2] = maxX;
			blockBBox[g + 3] = maxY;
		} while (p !== stop);
	}
	/** @param {Node} head @param {Node} tail */
	function growBlock(head, tail) {
		const g = head.z * 4;
		if (tail.x < blockBBox[g]) blockBBox[g] = tail.x;
		if (tail.y < blockBBox[g + 1]) blockBBox[g + 1] = tail.y;
		if (tail.x > blockBBox[g + 2]) blockBBox[g + 2] = tail.x;
		if (tail.y > blockBBox[g + 3]) blockBBox[g + 3] = tail.y;
	}
	/** @param {number} b @returns {Node} */
	function liveBlockStop(b) {
		let stop = blockStop[b];
		while (stop.prev.next !== stop) stop = stop.next;
		blockStop[b] = stop;
		return stop;
	}
	/** @param {number} b @returns {Node} */
	function liveBlockHead(b) {
		let head = blockHead[b];
		while (head.prev.next !== head) head = head.next;
		blockHead[b] = head;
		return head;
	}
	/** @param {Node} hole @param {Node} outerNode @returns {Node | null} */
	function findHoleBridge(hole, outerNode) {
		let p = outerNode;
		const hx = hole.x;
		const hy = hole.y;
		let qx = -Infinity;
		/** @type {Node | undefined} */
		let m;
		if (equals(hole, p)) return p;
		for (let b = 0, g = 0; b < numBlocks; b++, g += 4) {
			if (hy < blockBBox[g + 1] || hy > blockBBox[g + 3] || blockBBox[g] > hx || blockBBox[g + 2] <= qx) continue;
			const stop = liveBlockStop(b);
			p = liveBlockHead(b);
			do {
				if (p.prev.next === p) {
					if (equals(hole, p.next)) return p.next;
					else if (hy <= p.y && hy >= p.next.y && p.next.y !== p.y) {
						const x = p.x + (hy - p.y) * (p.next.x - p.x) / (p.next.y - p.y);
						if (x <= hx && x > qx) {
							qx = x;
							m = p.x < p.next.x ? p : p.next;
							if (x === hx) return m;
						}
					}
				}
				p = p.next;
			} while (p !== stop);
		}
		if (!m) return null;
		const mx = m.x;
		const my = m.y;
		const tminY = Math.min(hy, my);
		const tmaxY = Math.max(hy, my);
		let tanMin = Infinity;
		for (let b = 0, g = 0; b < numBlocks; b++, g += 4) {
			if (blockBBox[g + 2] < mx || blockBBox[g] > hx || blockBBox[g + 3] < tminY || blockBBox[g + 1] > tmaxY) continue;
			const stop = liveBlockStop(b);
			p = liveBlockHead(b);
			do {
				if (p.prev.next === p && hx >= p.x && p.x >= mx && hx !== p.x && pointInTriangle(hy < my ? hx : qx, hy, mx, my, hy < my ? qx : hx, hy, p.x, p.y)) {
					const tan = Math.abs(hy - p.y) / (hx - p.x);
					if ((locallyInside(p, hole) || p.y === hy && p.next.y === hy && p.next.x > hx) && (tan < tanMin || tan === tanMin && (p.x > m.x || p.x === m.x && sectorContainsSector(m, p)))) {
						m = p;
						tanMin = tan;
					}
				}
				p = p.next;
			} while (p !== stop);
		}
		return m;
	}
	/** @param {Node} m @param {Node} p @returns {boolean} */
	function sectorContainsSector(m, p) {
		return area(m.prev, m, p.prev) < 0 && area(p.next, m, m.next) < 0;
	}
	/** @type {Node[]} */
	const sortArr = [];
	/** @type {Node[]} */
	let sortBuf = [];
	let zArr = /* @__PURE__ */ new Uint32Array(0);
	let zBuf = /* @__PURE__ */ new Uint32Array(0);
	const counts = /* @__PURE__ */ new Uint32Array(256);
	/** @param {Node} start @param {number} minX @param {number} minY @param {number} invSize */
	function indexCurve(start, minX, minY, invSize) {
		let p = start;
		let n = 0;
		do {
			p.z = zOrder(p.x, p.y, minX, minY, invSize);
			sortArr[n++] = p;
			p = p.next;
		} while (p !== start);
		sortNodes(n);
		/** @type {Node | null} */
		let prev = null;
		for (let i = 0; i < n; i++) {
			const node = sortArr[i];
			node.prevZ = prev;
			if (prev) prev.nextZ = node;
			prev = node;
		}
		/** @type {Node} */ prev.nextZ = null;
	}
	/** @param {number} n */
	function sortNodes(n) {
		if (n <= 32) {
			for (let i = 1; i < n; i++) {
				const node = sortArr[i], z = node.z;
				let j = i - 1;
				while (j >= 0 && sortArr[j].z > z) {
					sortArr[j + 1] = sortArr[j];
					j--;
				}
				sortArr[j + 1] = node;
			}
			return;
		}
		if (zArr.length < n) {
			zArr = new Uint32Array(n);
			zBuf = new Uint32Array(n);
			sortBuf = new Array(n);
		}
		for (let i = 0; i < n; i++) zArr[i] = sortArr[i].z;
		radixPass(n, sortArr, zArr, sortBuf, zBuf, 0);
		radixPass(n, sortBuf, zBuf, sortArr, zArr, 8);
		radixPass(n, sortArr, zArr, sortBuf, zBuf, 16);
		radixPass(n, sortBuf, zBuf, sortArr, zArr, 24);
	}
	/** @param {number} n @param {Node[]} src @param {Uint32Array} srcZ @param {Node[]} dst @param {Uint32Array} dstZ @param {number} shift */
	function radixPass(n, src, srcZ, dst, dstZ, shift) {
		counts.fill(0);
		for (let i = 0; i < n; i++) counts[srcZ[i] >>> shift & 255]++;
		let sum = 0;
		for (let b = 0; b < 256; b++) {
			const c = counts[b];
			counts[b] = sum;
			sum += c;
		}
		for (let i = 0; i < n; i++) {
			const z = srcZ[i];
			const pos = counts[z >>> shift & 255]++;
			dst[pos] = src[i];
			dstZ[pos] = z;
		}
	}
	/** @param {number} x @param {number} y @param {number} minX @param {number} minY @param {number} invSize @returns {number} */
	function zOrder(x, y, minX, minY, invSize) {
		x = (x - minX) * invSize | 0;
		y = (y - minY) * invSize | 0;
		x = (x | x << 8) & 16711935;
		x = (x | x << 4) & 252645135;
		x = (x | x << 2) & 858993459;
		x = (x | x << 1) & 1431655765;
		y = (y | y << 8) & 16711935;
		y = (y | y << 4) & 252645135;
		y = (y | y << 2) & 858993459;
		y = (y | y << 1) & 1431655765;
		return x | y << 1;
	}
	/** @param {Node} start @returns {Node} */
	function getLeftmost(start) {
		let p = start, leftmost = start;
		do {
			if (p.x < leftmost.x || p.x === leftmost.x && p.y < leftmost.y) leftmost = p;
			p = p.next;
		} while (p !== start);
		return leftmost;
	}
	/** @param {number} ax @param {number} ay @param {number} bx @param {number} by @param {number} cx @param {number} cy @param {number} px @param {number} py @returns {boolean} */
	function pointInTriangle(ax, ay, bx, by, cx, cy, px, py) {
		return (cx - px) * (ay - py) >= (ax - px) * (cy - py) && (ax - px) * (by - py) >= (bx - px) * (ay - py) && (bx - px) * (cy - py) >= (cx - px) * (by - py);
	}
	/** @param {Node} a @param {Node} b @returns {boolean} true when the diagonal is valid */
	function isValidDiagonal(a, b) {
		const zeroLength = equals(a, b) && area(a.prev, a, a.next) > 0 && area(b.prev, b, b.next) > 0;
		return a.next.i !== b.i && (zeroLength || locallyInside(a, b) && locallyInside(b, a) && (area(a.prev, a, b.prev) !== 0 || area(a, b.prev, b) !== 0)) && !intersectsPolygon(a, b) && (zeroLength || middleInside(a, b));
	}
	/** @param {Node} p @param {Node} q @param {Node} r @returns {number} */
	function area(p, q, r) {
		return (q.y - p.y) * (r.x - q.x) - (q.x - p.x) * (r.y - q.y);
	}
	/** @param {Node} p1 @param {Node} p2 @returns {boolean} */
	function equals(p1, p2) {
		return p1.x === p2.x && p1.y === p2.y;
	}
	/** @param {Node} p1 @param {Node} q1 @param {Node} p2 @param {Node} q2 @param {boolean} [includeBoundary] @returns {boolean} */
	function intersects(p1, q1, p2, q2, includeBoundary = true) {
		const o1 = area(p1, q1, p2);
		const o2 = area(p1, q1, q2);
		const o3 = area(p2, q2, p1);
		const o4 = area(p2, q2, q1);
		if ((o1 > 0 && o2 < 0 || o1 < 0 && o2 > 0) && (o3 > 0 && o4 < 0 || o3 < 0 && o4 > 0)) return true;
		if (!includeBoundary) return false;
		if (o1 === 0 && onSegment(p1, p2, q1)) return true;
		if (o2 === 0 && onSegment(p1, q2, q1)) return true;
		if (o3 === 0 && onSegment(p2, p1, q2)) return true;
		if (o4 === 0 && onSegment(p2, q1, q2)) return true;
		return false;
	}
	/** @param {Node} p @param {Node} q @param {Node} r @returns {boolean} */
	function onSegment(p, q, r) {
		return q.x <= Math.max(p.x, r.x) && q.x >= Math.min(p.x, r.x) && q.y <= Math.max(p.y, r.y) && q.y >= Math.min(p.y, r.y);
	}
	/** @param {Node} a @param {Node} b @returns {boolean} */
	function intersectsPolygon(a, b) {
		const minX = Math.min(a.x, b.x);
		const maxX = Math.max(a.x, b.x);
		const minY = Math.min(a.y, b.y);
		const maxY = Math.max(a.y, b.y);
		let p = a;
		do {
			const n = p.next;
			if (p.x > maxX && n.x > maxX || p.x < minX && n.x < minX || p.y > maxY && n.y > maxY || p.y < minY && n.y < minY) {
				p = n;
				continue;
			}
			if (p.i !== a.i && n.i !== a.i && p.i !== b.i && n.i !== b.i && intersects(p, n, a, b)) return true;
			p = n;
		} while (p !== a);
		return false;
	}
	/** @param {Node} a @param {Node} b @returns {boolean} */
	function locallyInside(a, b) {
		return area(a.prev, a, a.next) < 0 ? area(a, b, a.next) >= 0 && area(a, a.prev, b) >= 0 : area(a, b, a.prev) < 0 || area(a, a.next, b) < 0;
	}
	/** @param {Node} a @param {Node} b @returns {boolean} */
	function middleInside(a, b) {
		let p = a;
		let inside = false;
		const px = (a.x + b.x) / 2;
		const py = (a.y + b.y) / 2;
		do {
			const n = p.next;
			if (p.y > py !== n.y > py && px < (n.x - p.x) * (py - p.y) / (n.y - p.y) + p.x) inside = !inside;
			p = n;
		} while (p !== a);
		return inside;
	}
	/** @param {Node} a @param {Node} b @returns {Node} */
	function splitPolygon(a, b) {
		const a2 = createNode(a.i, a.x, a.y), b2 = createNode(b.i, b.x, b.y), an = a.next, bp = b.prev;
		a.next = b;
		b.prev = a;
		a2.next = an;
		an.prev = a2;
		b2.next = a2;
		a2.prev = b2;
		bp.next = b2;
		b2.prev = bp;
		return b2;
	}
	/** @param {number} i @param {number} x @param {number} y @param {Node | null} last @returns {Node} */
	function insertNode(i, x, y, last) {
		const p = createNode(i, x, y);
		if (!last) {
			p.prev = p;
			p.next = p;
		} else {
			p.next = last.next;
			p.prev = last;
			last.next.prev = p;
			last.next = p;
		}
		return p;
	}
	/** @param {Node} p */
	function removeNode(p) {
		p.next.prev = p.prev;
		p.prev.next = p.next;
		if (p.prevZ) p.prevZ.nextZ = p.nextZ;
		if (p.nextZ) p.nextZ.prevZ = p.prevZ;
		if (indexActive) growBlock(p.prev, p.next);
	}
	/** @param {number} i @param {number} x @param {number} y @returns {Node} */
	function createNode(i, x, y) {
		return {
			i,
			x,
			y,
			prev: null,
			next: null,
			z: 0,
			prevZ: null,
			nextZ: null
		};
	}
	/** @param {ArrayLike<number>} data @param {number} start @param {number} end @param {number} dim @returns {number} */
	function signedArea(data, start, end, dim) {
		let sum = 0;
		for (let i = start, j = end - dim; i < end; i += dim) {
			sum += (data[j] - data[i]) * (data[i + 1] + data[j + 1]);
			j = i;
		}
		return sum;
	}

//#endregion
//#region ../../web/folia-major/src/components/visualizer/tempera/types.ts
/**
	* Every composition Tempera can cut to. Shots are half-phrase sized by default, so the list
	* has to be long enough that a paragraph rarely repeats one; `temperaShotProfiles.ts` carries
	* the layout region / camera / mood for each, and `temperaCompositions.ts` the drawing.
	*/
	const TEMPERA_SHOT_KINDS = [
		"duo-split",
		"quad-split",
		"tri-column",
		"thirds-stack",
		"checker-quad",
		"corner-wedge",
		"diagonal-halves",
		"cross-axis",
		"offset-halves",
		"stair-blocks",
		"pillar-gap",
		"corner-quad",
		"sliver-stack",
		"band-strip",
		"horizon-band",
		"deep-dive",
		"tone-ramp",
		"double-band",
		"tilt-band",
		"edge-rails",
		"gradient-wall",
		"terrace",
		"frame-window",
		"double-frame",
		"circle-window",
		"ladder-frame",
		"corner-brackets",
		"inset-box",
		"bracket-pair",
		"arch-window",
		"grid-cells",
		"keyhole",
		"poster-panel",
		"diamond-stack",
		"slash-poster",
		"arrow-wedge",
		"edge-bleed",
		"triangle-mass",
		"ribbon-cross",
		"half-disc",
		"stacked-slabs",
		"wedge-pair",
		"quiet-line",
		"starfield-dots",
		"ripple-lines",
		"hair-grid",
		"margin-rule",
		"dot-drift",
		"arc-sweep",
		"blank-page",
		"cinema-scope",
		"cinema-wide",
		"cinema-academy",
		"cinema-square",
		"cinema-portrait",
		"cinema-tall",
		"cinema-twin",
		"bubble-drift",
		"cloud-window",
		"heart-burst",
		"sparkle-field",
		"petal-arc",
		"scallop-band",
		"ribbon-loop",
		"round-plate",
		"halo-burst",
		"iris-hole",
		"slot-rail",
		"punch-row",
		"film-gate",
		"cross-vent",
		"louvre-slats",
		"ring-eye",
		"notch-stack",
		"wedge-gap",
		"dot-sieve",
		"sight-mark",
		"dial-scale",
		"chevron-run",
		"tally-column",
		"grid-focus",
		"axis-caps",
		"strobe-slats",
		"offset-plate",
		"radial-comb",
		"bracket-target",
		"flow-channel",
		"twin-channel",
		"reed-run",
		"taper-channel",
		"chain-ports",
		"dash-channel",
		"window-run",
		"bridge-span",
		"braid-channel",
		"port-ladder",
		"apex-mass",
		"ziggurat",
		"slab-wall",
		"cantilever",
		"pylon-pair",
		"bunker-slit",
		"plinth-stack",
		"buttress-run",
		"void-core",
		"shear-block",
		"ridge-line",
		"chasm",
		"overhang",
		"step-well",
		"pier-row",
		"revetment",
		"tower-crop",
		"lintel",
		"rubble-fan",
		"gnomon",
		"monogatari-card",
		"monogatari-rule",
		"monogatari-edge",
		"monogatari-stack",
		"monogatari-flash"
	];
	/**
	* Every transition is led by the large graphics or the camera; nothing dissolves or cuts
	* hard, because a dissolve reads as an edit and Tempera's compositions should hand off.
	*/
	const TEMPERA_TRANSITION_KINDS = [
		"block-wipe",
		"camera-pan",
		"shape-carry"
	];
	const TEMPERA_DECOR_MOTIFS = [
		"diamonds",
		"hatch-twin",
		"band-cross",
		"poster-diamond",
		"doodle"
	];

//#endregion
//#region ../../web/folia-major/src/components/visualizer/tempera/temperaShotProfiles.ts
	const region = (cx, cy, w, h, options = {}) => ({
		cx,
		cy,
		w,
		h,
		align: options.align ?? "center",
		rotation: options.rotation ?? 0,
		fontScale: options.fontScale ?? 1
	});
	const TEMPERA_SHOT_PROFILES = {
		"duo-split": {
			region: region(.5, .52, .86, .46),
			enter: {
				x: 0,
				y: 1.3
			},
			camera: {
				travel: .11,
				zoomStart: 1.06,
				zoomEnd: 1.13
			},
			mood: "neutral"
		},
		"quad-split": {
			region: region(.5, .5, .82, .4),
			enter: {
				x: .9,
				y: .9
			},
			camera: {
				travel: .09,
				zoomStart: 1.08,
				zoomEnd: 1.16
			},
			mood: "loud"
		},
		"tri-column": {
			region: region(.5, .5, .7, .5, { fontScale: .95 }),
			enter: {
				x: -1.2,
				y: 0
			},
			camera: {
				travel: .12,
				zoomStart: 1.04,
				zoomEnd: 1.12
			},
			mood: "neutral"
		},
		"thirds-stack": {
			region: region(.5, .5, .8, .28),
			enter: {
				x: 0,
				y: 1.1
			},
			camera: {
				travel: .13,
				zoomStart: 1.03,
				zoomEnd: 1.11
			},
			mood: "neutral"
		},
		"checker-quad": {
			region: region(.5, .5, .76, .36),
			enter: {
				x: .8,
				y: -.8
			},
			camera: {
				travel: .1,
				zoomStart: 1.1,
				zoomEnd: 1.02
			},
			mood: "loud"
		},
		"corner-wedge": {
			region: region(.44, .56, .68, .4, {
				align: "left",
				rotation: -.03
			}),
			enter: {
				x: -1.4,
				y: .5
			},
			camera: {
				travel: .1,
				zoomStart: 1.05,
				zoomEnd: 1.14
			},
			mood: "loud"
		},
		"diagonal-halves": {
			region: region(.5, .5, .78, .4, { rotation: -.075 }),
			enter: {
				x: 1.1,
				y: 1.1
			},
			camera: {
				travel: .12,
				zoomStart: 1.06,
				zoomEnd: 1.14
			},
			mood: "neutral"
		},
		"cross-axis": {
			region: region(.5, .5, .66, .3),
			enter: {
				x: 0,
				y: 1.2
			},
			camera: {
				travel: .08,
				zoomStart: 1.12,
				zoomEnd: 1.03
			},
			mood: "loud"
		},
		"offset-halves": {
			region: region(.5, .5, .8, .4),
			enter: {
				x: .9,
				y: .6
			},
			camera: {
				travel: .11,
				zoomStart: 1.05,
				zoomEnd: 1.13
			},
			mood: "neutral"
		},
		"stair-blocks": {
			region: region(.5, .5, .72, .38, { rotation: -.03 }),
			enter: {
				x: -1,
				y: .8
			},
			camera: {
				travel: .12,
				zoomStart: 1.06,
				zoomEnd: 1.14
			},
			mood: "loud"
		},
		"pillar-gap": {
			region: region(.5, .5, .34, .62, { fontScale: .8 }),
			enter: {
				x: 0,
				y: 1.2
			},
			camera: {
				travel: .07,
				zoomStart: 1.1,
				zoomEnd: 1.02
			},
			mood: "loud"
		},
		"corner-quad": {
			region: region(.46, .46, .68, .36),
			enter: {
				x: -.9,
				y: -.9
			},
			camera: {
				travel: .1,
				zoomStart: 1.07,
				zoomEnd: 1.15
			},
			mood: "neutral"
		},
		"sliver-stack": {
			region: region(.5, .5, .76, .3),
			enter: {
				x: 1.2,
				y: 0
			},
			camera: {
				travel: .13,
				zoomStart: 1.04,
				zoomEnd: 1.12
			},
			mood: "neutral"
		},
		"band-strip": {
			region: region(.5, .52, .78, .26, { fontScale: .92 }),
			enter: {
				x: .5,
				y: 1.05
			},
			camera: {
				travel: .12,
				zoomStart: 1.03,
				zoomEnd: 1.1
			},
			mood: "neutral"
		},
		"horizon-band": {
			region: region(.5, .36, .8, .3),
			enter: {
				x: 0,
				y: -1.1
			},
			camera: {
				travel: .14,
				zoomStart: 1.02,
				zoomEnd: 1.1
			},
			mood: "neutral"
		},
		"deep-dive": {
			region: region(.5, .58, .76, .34),
			enter: {
				x: 0,
				y: 1.6
			},
			camera: {
				travel: .16,
				zoomStart: 1.04,
				zoomEnd: 1.14
			},
			mood: "neutral"
		},
		"tone-ramp": {
			region: region(.5, .5, .82, .38),
			enter: {
				x: 1.2,
				y: .4
			},
			camera: {
				travel: .11,
				zoomStart: 1.05,
				zoomEnd: 1.12
			},
			mood: "neutral"
		},
		"double-band": {
			region: region(.5, .5, .78, .22, { fontScale: .88 }),
			enter: {
				x: .8,
				y: 0
			},
			camera: {
				travel: .12,
				zoomStart: 1.03,
				zoomEnd: 1.11
			},
			mood: "neutral"
		},
		"tilt-band": {
			region: region(.5, .5, .8, .26, { rotation: -.1 }),
			enter: {
				x: -1.1,
				y: .5
			},
			camera: {
				travel: .13,
				zoomStart: 1.05,
				zoomEnd: 1.13
			},
			mood: "neutral"
		},
		"edge-rails": {
			region: region(.5, .5, .74, .4),
			enter: {
				x: 0,
				y: 1
			},
			camera: {
				travel: .09,
				zoomStart: 1.02,
				zoomEnd: 1.09
			},
			mood: "quiet"
		},
		"gradient-wall": {
			region: region(.5, .44, .82, .36),
			enter: {
				x: .5,
				y: -.9
			},
			camera: {
				travel: .15,
				zoomStart: 1.04,
				zoomEnd: 1.13
			},
			mood: "neutral"
		},
		"terrace": {
			region: region(.46, .52, .72, .34, { align: "left" }),
			enter: {
				x: -1.2,
				y: .4
			},
			camera: {
				travel: .14,
				zoomStart: 1.05,
				zoomEnd: 1.12
			},
			mood: "neutral"
		},
		"frame-window": {
			region: region(.5, .5, .64, .5, { fontScale: .95 }),
			enter: {
				x: 0,
				y: .95
			},
			camera: {
				travel: .05,
				zoomStart: 1.14,
				zoomEnd: 1.03
			},
			mood: "neutral"
		},
		"double-frame": {
			region: region(.5, .5, .58, .4, { fontScale: .9 }),
			enter: {
				x: .6,
				y: .6
			},
			camera: {
				travel: .06,
				zoomStart: 1.12,
				zoomEnd: 1.02
			},
			mood: "neutral"
		},
		"circle-window": {
			region: region(.5, .5, .5, .34, { fontScale: .88 }),
			enter: {
				x: 0,
				y: .8
			},
			camera: {
				travel: .05,
				zoomStart: 1.16,
				zoomEnd: 1.04
			},
			mood: "quiet"
		},
		"ladder-frame": {
			region: region(.52, .5, .6, .4, {
				align: "left",
				fontScale: .9
			}),
			enter: {
				x: -.9,
				y: .6
			},
			camera: {
				travel: .07,
				zoomStart: 1.1,
				zoomEnd: 1.02
			},
			mood: "quiet"
		},
		"corner-brackets": {
			region: region(.5, .5, .56, .3, { fontScale: .85 }),
			enter: {
				x: 0,
				y: .6
			},
			camera: {
				travel: .04,
				zoomStart: 1.06,
				zoomEnd: 1.01
			},
			mood: "quiet"
		},
		"inset-box": {
			region: region(.5, .5, .6, .42, { fontScale: .92 }),
			enter: {
				x: 0,
				y: .8
			},
			camera: {
				travel: .06,
				zoomStart: 1.1,
				zoomEnd: 1.02
			},
			mood: "quiet"
		},
		"bracket-pair": {
			region: region(.5, .5, .54, .34, { fontScale: .88 }),
			enter: {
				x: 1,
				y: 0
			},
			camera: {
				travel: .05,
				zoomStart: 1.08,
				zoomEnd: 1.01
			},
			mood: "quiet"
		},
		"arch-window": {
			region: region(.5, .54, .5, .34, { fontScale: .86 }),
			enter: {
				x: 0,
				y: .9
			},
			camera: {
				travel: .06,
				zoomStart: 1.14,
				zoomEnd: 1.03
			},
			mood: "quiet"
		},
		"grid-cells": {
			region: region(.5, .5, .72, .22, { fontScale: .8 }),
			enter: {
				x: .7,
				y: .7
			},
			camera: {
				travel: .07,
				zoomStart: 1.06,
				zoomEnd: 1.13
			},
			mood: "neutral"
		},
		"keyhole": {
			region: region(.5, .6, .42, .3, { fontScale: .78 }),
			enter: {
				x: 0,
				y: 1
			},
			camera: {
				travel: .05,
				zoomStart: 1.12,
				zoomEnd: 1.02
			},
			mood: "quiet"
		},
		"poster-panel": {
			region: region(.4, .5, .58, .62, {
				align: "left",
				rotation: -.045
			}),
			enter: {
				x: -1.5,
				y: .35
			},
			camera: {
				travel: .08,
				zoomStart: 1.05,
				zoomEnd: 1.12
			},
			mood: "loud"
		},
		"diamond-stack": {
			region: region(.54, .5, .6, .42, {
				align: "right",
				rotation: .035
			}),
			enter: {
				x: 1.3,
				y: -.5
			},
			camera: {
				travel: .09,
				zoomStart: 1.07,
				zoomEnd: 1.15
			},
			mood: "loud"
		},
		"slash-poster": {
			region: region(.46, .5, .66, .44, {
				align: "left",
				rotation: -.09
			}),
			enter: {
				x: -1.2,
				y: 1
			},
			camera: {
				travel: .11,
				zoomStart: 1.06,
				zoomEnd: 1.14
			},
			mood: "loud"
		},
		"arrow-wedge": {
			region: region(.5, .34, .72, .26),
			enter: {
				x: 0,
				y: -1.2
			},
			camera: {
				travel: .13,
				zoomStart: 1.04,
				zoomEnd: 1.13
			},
			mood: "loud"
		},
		"edge-bleed": {
			region: region(.56, .5, .6, .44, { align: "left" }),
			enter: {
				x: 1.4,
				y: .3
			},
			camera: {
				travel: .1,
				zoomStart: 1.05,
				zoomEnd: 1.13
			},
			mood: "neutral"
		},
		"triangle-mass": {
			region: region(.5, .42, .68, .3),
			enter: {
				x: .8,
				y: -1
			},
			camera: {
				travel: .12,
				zoomStart: 1.05,
				zoomEnd: 1.14
			},
			mood: "loud"
		},
		"ribbon-cross": {
			region: region(.5, .5, .62, .3, { rotation: .05 }),
			enter: {
				x: -1,
				y: -.8
			},
			camera: {
				travel: .11,
				zoomStart: 1.08,
				zoomEnd: 1.16
			},
			mood: "loud"
		},
		"half-disc": {
			region: region(.44, .44, .6, .34, { align: "left" }),
			enter: {
				x: -1.3,
				y: .4
			},
			camera: {
				travel: .1,
				zoomStart: 1.06,
				zoomEnd: 1.14
			},
			mood: "loud"
		},
		"stacked-slabs": {
			region: region(.52, .5, .62, .4, { rotation: -.05 }),
			enter: {
				x: 1.1,
				y: .6
			},
			camera: {
				travel: .1,
				zoomStart: 1.07,
				zoomEnd: 1.15
			},
			mood: "loud"
		},
		"wedge-pair": {
			region: region(.5, .5, .44, .44, { fontScale: .82 }),
			enter: {
				x: 0,
				y: 1.1
			},
			camera: {
				travel: .09,
				zoomStart: 1.1,
				zoomEnd: 1.02
			},
			mood: "loud"
		},
		"quiet-line": {
			region: region(.5, .5, .6, .28, { fontScale: .58 }),
			enter: {
				x: 0,
				y: .7
			},
			camera: {
				travel: .03,
				zoomStart: 1,
				zoomEnd: 1.04
			},
			mood: "quiet"
		},
		"starfield-dots": {
			region: region(.5, .5, .62, .26, { fontScale: .66 }),
			enter: {
				x: .4,
				y: .5
			},
			camera: {
				travel: .04,
				zoomStart: 1.02,
				zoomEnd: 1.08
			},
			mood: "quiet"
		},
		"ripple-lines": {
			region: region(.5, .46, .66, .28, { fontScale: .72 }),
			enter: {
				x: 0,
				y: .9
			},
			camera: {
				travel: .06,
				zoomStart: 1.03,
				zoomEnd: 1.1
			},
			mood: "quiet"
		},
		"hair-grid": {
			region: region(.5, .5, .64, .26, { fontScale: .62 }),
			enter: {
				x: .4,
				y: .6
			},
			camera: {
				travel: .04,
				zoomStart: 1.01,
				zoomEnd: 1.06
			},
			mood: "quiet"
		},
		"margin-rule": {
			region: region(.54, .5, .62, .26, {
				align: "left",
				fontScale: .66
			}),
			enter: {
				x: -.8,
				y: .3
			},
			camera: {
				travel: .05,
				zoomStart: 1.02,
				zoomEnd: 1.07
			},
			mood: "quiet"
		},
		"dot-drift": {
			region: region(.5, .48, .6, .26, { fontScale: .68 }),
			enter: {
				x: .6,
				y: .6
			},
			camera: {
				travel: .05,
				zoomStart: 1.03,
				zoomEnd: 1.09
			},
			mood: "quiet"
		},
		"arc-sweep": {
			region: region(.5, .5, .6, .26, { fontScale: .7 }),
			enter: {
				x: 0,
				y: .8
			},
			camera: {
				travel: .06,
				zoomStart: 1.04,
				zoomEnd: 1.1
			},
			mood: "quiet"
		},
		"blank-page": {
			region: region(.5, .5, .56, .24, { fontScale: .6 }),
			enter: {
				x: 0,
				y: .5
			},
			camera: {
				travel: .03,
				zoomStart: 1,
				zoomEnd: 1.03
			},
			mood: "quiet"
		},
		"cinema-scope": {
			region: region(.5, .5, .74, .22, { fontScale: .86 }),
			enter: {
				x: .9,
				y: 0
			},
			camera: {
				travel: .09,
				zoomStart: 1.04,
				zoomEnd: 1.11
			},
			mood: "neutral"
		},
		"cinema-wide": {
			region: region(.5, .5, .7, .3, { fontScale: .9 }),
			enter: {
				x: 0,
				y: .9
			},
			camera: {
				travel: .08,
				zoomStart: 1.06,
				zoomEnd: 1.13
			},
			mood: "neutral"
		},
		"cinema-academy": {
			region: region(.5, .5, .5, .4, { fontScale: .88 }),
			enter: {
				x: 0,
				y: .8
			},
			camera: {
				travel: .06,
				zoomStart: 1.1,
				zoomEnd: 1.02
			},
			mood: "quiet"
		},
		"cinema-square": {
			region: region(.5, .5, .42, .4, { fontScale: .84 }),
			enter: {
				x: .7,
				y: .7
			},
			camera: {
				travel: .05,
				zoomStart: 1.12,
				zoomEnd: 1.03
			},
			mood: "quiet"
		},
		"cinema-portrait": {
			region: region(.5, .5, .32, .46, { fontScale: .8 }),
			enter: {
				x: 0,
				y: 1
			},
			camera: {
				travel: .06,
				zoomStart: 1.08,
				zoomEnd: 1.16
			},
			mood: "neutral"
		},
		"cinema-tall": {
			region: region(.5, .5, .24, .5, { fontScale: .72 }),
			enter: {
				x: .8,
				y: .4
			},
			camera: {
				travel: .07,
				zoomStart: 1.05,
				zoomEnd: 1.14
			},
			mood: "neutral"
		},
		"cinema-twin": {
			region: region(.33, .5, .3, .32, { fontScale: .76 }),
			enter: {
				x: -.9,
				y: .3
			},
			camera: {
				travel: .08,
				zoomStart: 1.06,
				zoomEnd: 1.13
			},
			mood: "neutral"
		},
		"bubble-drift": {
			region: region(.5, .5, .72, .36, { fontScale: .92 }),
			enter: {
				x: .5,
				y: 1
			},
			camera: {
				travel: .09,
				zoomStart: 1.05,
				zoomEnd: 1.12
			},
			mood: "neutral"
		},
		"cloud-window": {
			region: region(.5, .52, .46, .3, { fontScale: .84 }),
			enter: {
				x: 0,
				y: .8
			},
			camera: {
				travel: .05,
				zoomStart: 1.14,
				zoomEnd: 1.03
			},
			mood: "quiet"
		},
		"heart-burst": {
			region: region(.5, .52, .5, .3, { fontScale: .95 }),
			enter: {
				x: .8,
				y: .8
			},
			camera: {
				travel: .1,
				zoomStart: 1.08,
				zoomEnd: 1.16
			},
			mood: "loud"
		},
		"sparkle-field": {
			region: region(.5, .48, .64, .26, { fontScale: .7 }),
			enter: {
				x: .4,
				y: .6
			},
			camera: {
				travel: .04,
				zoomStart: 1.02,
				zoomEnd: 1.08
			},
			mood: "quiet"
		},
		"petal-arc": {
			region: region(.5, .6, .7, .3, { fontScale: .9 }),
			enter: {
				x: 0,
				y: 1.1
			},
			camera: {
				travel: .1,
				zoomStart: 1.04,
				zoomEnd: 1.12
			},
			mood: "neutral"
		},
		"scallop-band": {
			region: region(.5, .5, .76, .24, { fontScale: .88 }),
			enter: {
				x: .7,
				y: 0
			},
			camera: {
				travel: .12,
				zoomStart: 1.03,
				zoomEnd: 1.1
			},
			mood: "neutral"
		},
		"ribbon-loop": {
			region: region(.5, .5, .68, .22, {
				rotation: .04,
				fontScale: .86
			}),
			enter: {
				x: -1.1,
				y: .6
			},
			camera: {
				travel: .11,
				zoomStart: 1.07,
				zoomEnd: 1.15
			},
			mood: "loud"
		},
		"round-plate": {
			region: region(.5, .5, .56, .36, { fontScale: .86 }),
			enter: {
				x: 0,
				y: .7
			},
			camera: {
				travel: .05,
				zoomStart: 1.1,
				zoomEnd: 1.02
			},
			mood: "quiet"
		},
		"halo-burst": {
			region: region(.5, .5, .46, .3, { fontScale: .95 }),
			enter: {
				x: 0,
				y: .9
			},
			camera: {
				travel: .08,
				zoomStart: 1.12,
				zoomEnd: 1.02
			},
			mood: "loud"
		},
		"iris-hole": {
			region: region(.28, .68, .44, .28, { fontScale: .78 }),
			enter: {
				x: -1,
				y: .6
			},
			camera: {
				travel: .09,
				zoomStart: 1.06,
				zoomEnd: 1.14
			},
			mood: "loud"
		},
		"slot-rail": {
			region: region(.5, .66, .72, .3, { fontScale: .9 }),
			enter: {
				x: .6,
				y: .9
			},
			camera: {
				travel: .12,
				zoomStart: 1.04,
				zoomEnd: 1.11
			},
			mood: "neutral"
		},
		"punch-row": {
			region: region(.5, .5, .76, .34, { fontScale: .92 }),
			enter: {
				x: 1.1,
				y: 0
			},
			camera: {
				travel: .13,
				zoomStart: 1.03,
				zoomEnd: 1.1
			},
			mood: "neutral"
		},
		"film-gate": {
			region: region(.5, .82, .66, .2, { fontScale: .8 }),
			enter: {
				x: 0,
				y: 1.1
			},
			camera: {
				travel: .08,
				zoomStart: 1.08,
				zoomEnd: 1.02
			},
			mood: "loud"
		},
		"cross-vent": {
			region: region(.5, .82, .66, .2, { fontScale: .8 }),
			enter: {
				x: 0,
				y: 1
			},
			camera: {
				travel: .07,
				zoomStart: 1.1,
				zoomEnd: 1.02
			},
			mood: "loud"
		},
		"louvre-slats": {
			region: region(.5, .8, .7, .22, { fontScale: .82 }),
			enter: {
				x: .8,
				y: .7
			},
			camera: {
				travel: .11,
				zoomStart: 1.05,
				zoomEnd: 1.13
			},
			mood: "neutral"
		},
		"ring-eye": {
			region: region(.5, .5, .26, .18, { fontScale: .62 }),
			enter: {
				x: 0,
				y: .7
			},
			camera: {
				travel: .05,
				zoomStart: 1.14,
				zoomEnd: 1.03
			},
			mood: "quiet"
		},
		"notch-stack": {
			region: region(.32, .5, .5, .34, { fontScale: .84 }),
			enter: {
				x: -1.1,
				y: .5
			},
			camera: {
				travel: .1,
				zoomStart: 1.05,
				zoomEnd: 1.13
			},
			mood: "neutral"
		},
		"wedge-gap": {
			region: region(.5, .78, .7, .24, { fontScale: .82 }),
			enter: {
				x: 0,
				y: 1.2
			},
			camera: {
				travel: .12,
				zoomStart: 1.06,
				zoomEnd: 1.14
			},
			mood: "loud"
		},
		"dot-sieve": {
			region: region(.75, .5, .38, .3, { fontScale: .78 }),
			enter: {
				x: .9,
				y: .4
			},
			camera: {
				travel: .06,
				zoomStart: 1.03,
				zoomEnd: 1.09
			},
			mood: "quiet"
		},
		"sight-mark": {
			region: region(.5, .5, .46, .2, { fontScale: .85 }),
			enter: {
				x: 0,
				y: .8
			},
			camera: {
				travel: .06,
				zoomStart: 1.12,
				zoomEnd: 1.02
			},
			mood: "loud"
		},
		"dial-scale": {
			region: region(.5, .5, .36, .2, { fontScale: .8 }),
			enter: {
				x: .7,
				y: .5
			},
			camera: {
				travel: .05,
				zoomStart: 1.1,
				zoomEnd: 1.02
			},
			mood: "neutral"
		},
		"chevron-run": {
			region: region(.5, .52, .72, .3, { fontScale: .9 }),
			enter: {
				x: 0,
				y: -1.1
			},
			camera: {
				travel: .14,
				zoomStart: 1.04,
				zoomEnd: 1.12
			},
			mood: "neutral"
		},
		"tally-column": {
			region: region(.38, .5, .6, .32, {
				align: "left",
				fontScale: .76
			}),
			enter: {
				x: -.9,
				y: .3
			},
			camera: {
				travel: .05,
				zoomStart: 1.02,
				zoomEnd: 1.08
			},
			mood: "quiet"
		},
		"grid-focus": {
			region: region(.5, .5, .62, .22, { fontScale: .85 }),
			enter: {
				x: .8,
				y: .6
			},
			camera: {
				travel: .09,
				zoomStart: 1.05,
				zoomEnd: 1.13
			},
			mood: "neutral"
		},
		"axis-caps": {
			region: region(.5, .72, .66, .24, { fontScale: .82 }),
			enter: {
				x: 0,
				y: 1.2
			},
			camera: {
				travel: .13,
				zoomStart: 1.06,
				zoomEnd: 1.15
			},
			mood: "loud"
		},
		"strobe-slats": {
			region: region(.5, .5, .3, .36, { fontScale: .8 }),
			enter: {
				x: 1,
				y: 0
			},
			camera: {
				travel: .1,
				zoomStart: 1.08,
				zoomEnd: 1.16
			},
			mood: "loud"
		},
		"offset-plate": {
			region: region(.48, .62, .4, .2, { fontScale: .85 }),
			enter: {
				x: -.9,
				y: -.6
			},
			camera: {
				travel: .09,
				zoomStart: 1.07,
				zoomEnd: 1.15
			},
			mood: "loud"
		},
		"radial-comb": {
			region: region(.42, .4, .62, .3, { fontScale: .88 }),
			enter: {
				x: -1.2,
				y: -.4
			},
			camera: {
				travel: .11,
				zoomStart: 1.05,
				zoomEnd: 1.13
			},
			mood: "neutral"
		},
		"bracket-target": {
			region: region(.5, .28, .66, .24, { fontScale: .85 }),
			enter: {
				x: 0,
				y: -.9
			},
			camera: {
				travel: .06,
				zoomStart: 1.04,
				zoomEnd: 1.1
			},
			mood: "quiet"
		},
		"flow-channel": {
			region: region(.32, .5, .5, .3, { fontScale: .85 }),
			enter: {
				x: -1,
				y: .5
			},
			camera: {
				travel: .13,
				zoomStart: 1.04,
				zoomEnd: 1.12
			},
			mood: "neutral"
		},
		"twin-channel": {
			region: region(.5, .5, .4, .3, { fontScale: .85 }),
			enter: {
				x: 0,
				y: 1.2
			},
			camera: {
				travel: .14,
				zoomStart: 1.03,
				zoomEnd: 1.11
			},
			mood: "neutral"
		},
		"reed-run": {
			region: region(.5, .5, .3, .3, { fontScale: .72 }),
			enter: {
				x: 0,
				y: .9
			},
			camera: {
				travel: .1,
				zoomStart: 1.02,
				zoomEnd: 1.09
			},
			mood: "quiet"
		},
		"taper-channel": {
			region: region(.28, .5, .42, .3, { fontScale: .82 }),
			enter: {
				x: -1.1,
				y: .6
			},
			camera: {
				travel: .12,
				zoomStart: 1.05,
				zoomEnd: 1.14
			},
			mood: "neutral"
		},
		"chain-ports": {
			region: region(.32, .5, .5, .32, { fontScale: .8 }),
			enter: {
				x: -.8,
				y: .7
			},
			camera: {
				travel: .11,
				zoomStart: 1.03,
				zoomEnd: 1.1
			},
			mood: "quiet"
		},
		"dash-channel": {
			region: region(.3, .5, .46, .3, { fontScale: .84 }),
			enter: {
				x: 0,
				y: 1.3
			},
			camera: {
				travel: .15,
				zoomStart: 1.04,
				zoomEnd: 1.13
			},
			mood: "neutral"
		},
		"window-run": {
			region: region(.28, .5, .42, .3, { fontScale: .82 }),
			enter: {
				x: 0,
				y: 1.2
			},
			camera: {
				travel: .14,
				zoomStart: 1.04,
				zoomEnd: 1.12
			},
			mood: "neutral"
		},
		"bridge-span": {
			region: region(.5, .5, .72, .16, { fontScale: .85 }),
			enter: {
				x: 1.2,
				y: 0
			},
			camera: {
				travel: .12,
				zoomStart: 1.08,
				zoomEnd: 1.16
			},
			mood: "loud"
		},
		"braid-channel": {
			region: region(.5, .5, .34, .3, { fontScale: .8 }),
			enter: {
				x: 0,
				y: 1.1
			},
			camera: {
				travel: .13,
				zoomStart: 1.06,
				zoomEnd: 1.15
			},
			mood: "loud"
		},
		"port-ladder": {
			region: region(.3, .5, .46, .3, { fontScale: .78 }),
			enter: {
				x: -.9,
				y: .4
			},
			camera: {
				travel: .09,
				zoomStart: 1.02,
				zoomEnd: 1.09
			},
			mood: "quiet"
		},
		"apex-mass": {
			region: region(.5, .3, .62, .2, { fontScale: .9 }),
			enter: {
				x: 0,
				y: -1
			},
			camera: {
				travel: .12,
				zoomStart: 1.04,
				zoomEnd: 1.13
			},
			mood: "loud"
		},
		"ziggurat": {
			region: region(.5, .32, .5, .18, { fontScale: .85 }),
			enter: {
				x: 0,
				y: -.9
			},
			camera: {
				travel: .1,
				zoomStart: 1.06,
				zoomEnd: 1.15
			},
			mood: "loud"
		},
		"slab-wall": {
			region: region(.36, .5, .5, .24, { fontScale: .88 }),
			enter: {
				x: 1.2,
				y: 0
			},
			camera: {
				travel: .11,
				zoomStart: 1.05,
				zoomEnd: 1.12
			},
			mood: "neutral"
		},
		"cantilever": {
			region: region(.42, .42, .6, .14, { fontScale: .8 }),
			enter: {
				x: -1.3,
				y: 0
			},
			camera: {
				travel: .13,
				zoomStart: 1.04,
				zoomEnd: 1.12
			},
			mood: "loud"
		},
		"pylon-pair": {
			region: region(.5, .22, .7, .12, { fontScale: .75 }),
			enter: {
				x: 0,
				y: -.8
			},
			camera: {
				travel: .09,
				zoomStart: 1.08,
				zoomEnd: 1.02
			},
			mood: "loud"
		},
		"bunker-slit": {
			region: region(.5, .48, .72, .2, { fontScale: .86 }),
			enter: {
				x: .9,
				y: .4
			},
			camera: {
				travel: .08,
				zoomStart: 1.06,
				zoomEnd: 1.13
			},
			mood: "neutral"
		},
		"plinth-stack": {
			region: region(.5, .32, .44, .16, { fontScale: .82 }),
			enter: {
				x: 0,
				y: -.8
			},
			camera: {
				travel: .1,
				zoomStart: 1.05,
				zoomEnd: 1.13
			},
			mood: "neutral"
		},
		"buttress-run": {
			region: region(.5, .26, .7, .2, { fontScale: .86 }),
			enter: {
				x: .8,
				y: -.8
			},
			camera: {
				travel: .12,
				zoomStart: 1.03,
				zoomEnd: 1.11
			},
			mood: "loud"
		},
		"void-core": {
			region: region(.5, .2, .66, .18, { fontScale: .84 }),
			enter: {
				x: 0,
				y: -1.1
			},
			camera: {
				travel: .11,
				zoomStart: 1.07,
				zoomEnd: 1.15
			},
			mood: "loud"
		},
		"shear-block": {
			region: region(.5, .5, .56, .2, { fontScale: .88 }),
			enter: {
				x: 1,
				y: .5
			},
			camera: {
				travel: .12,
				zoomStart: 1.06,
				zoomEnd: 1.14
			},
			mood: "neutral"
		},
		"ridge-line": {
			region: region(.5, .42, .72, .2, { fontScale: .9 }),
			enter: {
				x: 0,
				y: -1
			},
			camera: {
				travel: .14,
				zoomStart: 1.03,
				zoomEnd: 1.12
			},
			mood: "neutral"
		},
		"chasm": {
			region: region(.25, .5, .38, .24, { fontScale: .82 }),
			enter: {
				x: -1.2,
				y: .4
			},
			camera: {
				travel: .1,
				zoomStart: 1.06,
				zoomEnd: 1.14
			},
			mood: "loud"
		},
		"overhang": {
			region: region(.44, .58, .6, .22, { fontScale: .88 }),
			enter: {
				x: 0,
				y: 1.1
			},
			camera: {
				travel: .11,
				zoomStart: 1.08,
				zoomEnd: 1.02
			},
			mood: "loud"
		},
		"step-well": {
			region: region(.5, .26, .66, .2, { fontScale: .85 }),
			enter: {
				x: 0,
				y: -.9
			},
			camera: {
				travel: .09,
				zoomStart: 1.04,
				zoomEnd: 1.12
			},
			mood: "neutral"
		},
		"pier-row": {
			region: region(.5, .4, .76, .14, { fontScale: .82 }),
			enter: {
				x: 1.1,
				y: 0
			},
			camera: {
				travel: .13,
				zoomStart: 1.03,
				zoomEnd: 1.1
			},
			mood: "neutral"
		},
		"revetment": {
			region: region(.46, .3, .6, .2, { fontScale: .86 }),
			enter: {
				x: -1,
				y: -.6
			},
			camera: {
				travel: .12,
				zoomStart: 1.05,
				zoomEnd: 1.13
			},
			mood: "neutral"
		},
		"tower-crop": {
			region: region(.28, .5, .44, .24, {
				align: "left",
				fontScale: .74
			}),
			enter: {
				x: -.8,
				y: .3
			},
			camera: {
				travel: .06,
				zoomStart: 1.02,
				zoomEnd: 1.09
			},
			mood: "quiet"
		},
		"lintel": {
			region: region(.5, .5, .76, .16, { fontScale: .9 }),
			enter: {
				x: -1.1,
				y: 0
			},
			camera: {
				travel: .1,
				zoomStart: 1.02,
				zoomEnd: 1.08
			},
			mood: "quiet"
		},
		"rubble-fan": {
			region: region(.56, .4, .5, .22, { fontScale: .84 }),
			enter: {
				x: 1.2,
				y: -.5
			},
			camera: {
				travel: .13,
				zoomStart: 1.07,
				zoomEnd: 1.16
			},
			mood: "loud"
		},
		"gnomon": {
			region: region(.66, .34, .46, .2, { fontScale: .78 }),
			enter: {
				x: .9,
				y: -.4
			},
			camera: {
				travel: .07,
				zoomStart: 1.03,
				zoomEnd: 1.1
			},
			mood: "quiet"
		},
		"monogatari-card": {
			region: region(.5, .5, .78, .5, { fontScale: 1.15 }),
			enter: {
				x: 0,
				y: .7
			},
			camera: {
				travel: .03,
				zoomStart: 1.02,
				zoomEnd: 1.07
			},
			mood: "quiet",
			sharedDecor: false
		},
		"monogatari-rule": {
			region: region(.5, .46, .76, .4, { fontScale: 1.05 }),
			enter: {
				x: .6,
				y: 0
			},
			camera: {
				travel: .04,
				zoomStart: 1.03,
				zoomEnd: 1.09
			},
			mood: "quiet",
			sharedDecor: false
		},
		"monogatari-edge": {
			region: region(.52, .5, .72, .46, {
				align: "left",
				fontScale: 1.05
			}),
			enter: {
				x: -.8,
				y: 0
			},
			camera: {
				travel: .05,
				zoomStart: 1.02,
				zoomEnd: 1.08
			},
			mood: "neutral",
			sharedDecor: false
		},
		"monogatari-stack": {
			region: region(.5, .5, .44, .62, { fontScale: 1 }),
			enter: {
				x: 0,
				y: .9
			},
			camera: {
				travel: .04,
				zoomStart: 1.05,
				zoomEnd: 1.12
			},
			mood: "quiet",
			sharedDecor: false
		},
		"monogatari-flash": {
			region: region(.5, .5, .82, .44, { fontScale: 1.3 }),
			enter: {
				x: 0,
				y: .5
			},
			camera: {
				travel: .02,
				zoomStart: 1.08,
				zoomEnd: 1.01
			},
			mood: "loud",
			sharedDecor: false
		}
	};
	const resolveTemperaShotProfile = (kind) => TEMPERA_SHOT_PROFILES[kind] ?? TEMPERA_SHOT_PROFILES["duo-split"];
	/** Compositions a paragraph of this character is allowed to cut to. */
	const resolveTemperaShotCandidates = (moods) => {
		const candidates = TEMPERA_SHOT_KINDS.filter((kind) => moods.includes(resolveTemperaShotProfile(kind).mood));
		return candidates.length > 0 ? candidates : TEMPERA_SHOT_KINDS;
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/tempera/temperaRandom.ts
	const chooseWithoutRepeat$1 = (choices, seed, previous) => {
		const start = hashVisualizerSeed(seed) % choices.length;
		for (let offset = 0; offset < choices.length; offset += 1) {
			const candidate = choices[(start + offset) % choices.length];
			if (candidate !== previous) return candidate;
		}
		return choices[start];
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/tempera/temperaProgram.ts
	const clamp$4 = (value, min, max) => Math.min(max, Math.max(min, value));
	const median$1 = (values) => {
		if (values.length === 0) return .5;
		const sorted = [...values].sort((a, b) => a - b);
		const middle = Math.floor(sorted.length / 2);
		return sorted.length % 2 === 0 ? ((sorted[middle - 1] ?? sorted[middle]) + sorted[middle]) / 2 : sorted[middle];
	};
	const resolveTemperaParagraphGapThreshold = (lines) => {
		const gaps = lines.slice(1).map((line, index) => line.startTime - Math.min(getLineRenderEndTime(lines[index]), line.startTime)).filter((gap) => gap > 0);
		return clamp$4(median$1(gaps) * 2.5, 1.25, 3.5);
	};
	const PUNCTUATION_ONLY = /^[\s\p{P}\p{S}]+$/u;
	const metadataChanged$1 = (previous, next) => previous.blockIndex !== void 0 && next.blockIndex !== void 0 && previous.blockIndex !== next.blockIndex || previous.songPart !== void 0 && next.songPart !== void 0 && previous.songPart !== next.songPart;
	const buildTemperaSegments = (line) => {
		if (!line.fullText) return [];
		const timeline = buildLineGraphemeTimeline(line);
		let cursor = 0;
		const ranges = splitLyricGraphemes(line.fullText).map((grapheme) => {
			const range = {
				start: cursor,
				end: cursor + grapheme.length
			};
			cursor = range.end;
			return range;
		});
		const parts = segmentLyricWords(line);
		const segments = parts.map((part, index) => {
			const startOffset = part.index;
			const endOffset = parts[index + 1]?.index ?? line.fullText.length;
			const graphemes = ranges.flatMap((range, rangeIndex) => range.end > startOffset && range.start < endOffset ? [rangeIndex] : []).map((graphemeIndex) => timeline[graphemeIndex]).filter(Boolean);
			return {
				text: line.fullText.slice(startOffset, endOffset),
				startOffset,
				endOffset,
				graphemes,
				startTime: graphemes[0]?.startTime ?? line.startTime,
				endTime: graphemes[graphemes.length - 1]?.endTime ?? line.endTime,
				isWordLike: part.isWordLike
			};
		});
		const sticky = [];
		for (const segment of segments) {
			const previous = sticky[sticky.length - 1];
			if (previous && !segment.isWordLike && !/^\s+$/u.test(segment.text)) {
				previous.text += segment.text;
				previous.endOffset = segment.endOffset;
				const tail = previous.endTime;
				const merged = segment.graphemes.map((grapheme) => grapheme.endTime > grapheme.startTime ? grapheme : {
					...grapheme,
					startTime: tail,
					endTime: tail
				});
				previous.graphemes.push(...merged);
				previous.endTime = Math.max(previous.endTime, ...merged.map((grapheme) => grapheme.endTime));
			} else sticky.push({
				...segment,
				graphemes: [...segment.graphemes]
			});
		}
		return sticky;
	};
	const splitOversizedDraft$1 = (draft) => {
		const output = [];
		let remaining = draft.lines;
		let boundary = draft.boundary;
		let loopGuard = 0;
		while (remaining.length > 6 || remaining.length > 1 && remaining.at(-1).renderEndTime - remaining[0].line.startTime > 18) {
			if (loopGuard++ > 1e3) {
				console.error("splitOversizedDraft: Infinite loop detected, breaking");
				break;
			}
			const rawSplitIndex = remaining.slice(2, -1).map((line, offset) => ({
				splitIndex: offset + 2,
				gap: line.line.startTime - remaining[offset + 1].renderEndTime
			})).filter((candidate) => !Number.isNaN(candidate.gap)).sort((a, b) => b.gap - a.gap)[0]?.splitIndex ?? Math.min(4, remaining.length - 1);
			const splitIndex = Math.max(1, rawSplitIndex);
			output.push({
				lines: remaining.slice(0, splitIndex),
				boundary
			});
			remaining = remaining.slice(splitIndex);
			boundary = output.at(-1).lines.length >= 6 ? "line-cap" : "duration-cap";
		}
		output.push({
			lines: remaining,
			boundary
		});
		return output;
	};
	const classifyParagraph$1 = (lines, index, total) => {
		if (lines.some((item) => item.line.isChorus || /chorus|副歌/i.test(item.line.songPart ?? ""))) return "chorus";
		if (lines.some((item) => /bridge|break|間奏|ブリッジ/i.test(item.line.songPart ?? ""))) return "break";
		if (index === total - 1) return "outro";
		const duration = lines.at(-1).renderEndTime - lines[0].line.startTime;
		const segmentCount = lines.reduce((sum, line) => sum + line.segments.filter((segment) => segment.isWordLike).length, 0);
		const punctuationCount = lines.reduce((sum, line) => sum + (line.line.fullText.match(/[!?！？…]/g)?.length ?? 0), 0);
		if (duration <= 3.5 || segmentCount <= 3) return "breath";
		if (punctuationCount >= 2 || segmentCount / Math.max(duration, 1) > 2.5) return "lift";
		return "verse";
	};
	const isRenderableSegment = (segment) => segment.text.trim().length > 0 && segment.graphemes.length > 0;
	const SINGLE_HAN = /^\p{Script=Han}$/u;
	/** How long the single-character glue below may hold a chunk open (the usual close is 2.2s). */
	const SINGLE_HAN_GLUE_MAX_SECONDS = 3.2;
	/** A one-character Chinese word (我, 想, 再), the unit the chunker must not cut between. */
	const isSingleHanWord = (segment) => segment.isWordLike && SINGLE_HAN.test(segment.text);
	/**
	* Builds the lyric chunks that become shots. The default closes a chunk once it holds a seeded
	* 2..4 words or has run for ~2.2s; whole-line mode keeps every renderable segment in one chunk.
	*/
	const buildShotChunks = (lines, seed, paragraphIndex, wholeLineLyrics) => {
		const chunks = [];
		lines.forEach((line) => {
			const usable = line.segments.map((segment, index) => ({
				segment,
				index
			})).filter((entry) => isRenderableSegment(entry.segment));
			if (usable.length === 0) return;
			if (wholeLineLyrics) {
				const first = usable[0];
				const last = usable.at(-1);
				chunks.push({
					lineIndex: line.sourceIndex,
					segmentStart: first.index,
					segmentEnd: last.index + 1,
					startTime: first.segment.startTime,
					endTime: Math.max(last.segment.endTime, first.segment.startTime + .2),
					lyricEndTime: Math.max(last.segment.endTime, first.segment.startTime + .2)
				});
				return;
			}
			let segmentStart = usable[0].index;
			let startTime = usable[0].segment.startTime;
			let words = 0;
			usable.forEach((entry, order) => {
				words += 1;
				const chunkSeed = hashVisualizerSeed(`${seed}:${paragraphIndex}:${line.sourceIndex}:${chunks.length}`);
				const target = 2 + Math.floor(visualizerHash01(chunkSeed, 1, 179) * 3);
				const spent = entry.segment.endTime - startTime;
				const isLast = order === usable.length - 1;
				if (!isLast && words < target && spent < 2.2) return;
				const next = usable[order + 1];
				if (!isLast && spent < SINGLE_HAN_GLUE_MAX_SECONDS && next && isSingleHanWord(entry.segment) && isSingleHanWord(next.segment) && next.segment.startOffset === entry.segment.endOffset) return;
				chunks.push({
					lineIndex: line.sourceIndex,
					segmentStart,
					segmentEnd: entry.index + 1,
					startTime,
					endTime: Math.max(entry.segment.endTime, startTime + .2),
					lyricEndTime: Math.max(entry.segment.endTime, startTime + .2)
				});
				if (next) {
					segmentStart = next.index;
					startTime = next.segment.startTime;
					words = 0;
				}
			});
		});
		return chunks.map((chunk, index) => ({
			...chunk,
			endTime: Math.max(chunk.startTime + .2, chunks[index + 1]?.startTime ?? chunk.endTime)
		}));
	};
	const buildCameraKeys = (kind, seed, flowAngle) => {
		const jitterX = (visualizerHash01(seed, 1, 11) - .5) * .02;
		const jitterY = (visualizerHash01(seed, 2, 23) - .5) * .02;
		const jitterZoom = visualizerHash01(seed, 3, 37) * .025;
		const jitterRotation = (visualizerHash01(seed, 4, 51) - .5) * .012;
		const { camera } = resolveTemperaShotProfile(kind);
		const travelX = Math.cos(flowAngle) * camera.travel;
		const travelY = Math.sin(flowAngle) * camera.travel;
		return {
			start: {
				x: -travelX / 2 + jitterX,
				y: -travelY / 2 + jitterY,
				zoom: camera.zoomStart + jitterZoom,
				rotation: jitterRotation
			},
			end: {
				x: travelX / 2 + jitterX,
				y: travelY / 2 + jitterY,
				zoom: camera.zoomEnd + jitterZoom,
				rotation: -jitterRotation
			}
		};
	};
	const buildDecorFragments = (pool, count, seed) => {
		const chars = Array.from(pool).filter((char) => char.trim().length > 0 && !PUNCTUATION_ONLY.test(char));
		if (chars.length === 0 || count <= 0) return [];
		return Array.from({ length: count }, (_, index) => {
			const char = chars[Math.floor(visualizerHash01(seed, index, 13) * chars.length) % chars.length];
			const onLeft = visualizerHash01(seed, index, 29) > .5;
			const edge = .03 + visualizerHash01(seed, index, 41) * .13;
			return {
				char,
				x: onLeft ? edge : 1 - edge,
				y: .08 + visualizerHash01(seed, index, 53) * .84,
				rotation: (visualizerHash01(seed, index, 67) - .5) * .5,
				scale: .26 + visualizerHash01(seed, index, 71) * .2
			};
		});
	};
	/**
	* Picks the oversized decorative word for a shot. It is drawn from the words this shot is
	* *not* setting, so the watermark reads as the phrase around the line rather than as a
	* duplicate of it. Loud compositions skip it: they already carry a dominant shape.
	*/
	const buildDecorWatermark = (pool, seed, allowed) => {
		const words = pool.map((word) => word.trim()).filter((word) => word.length > 0 && word.length <= 12);
		if (!allowed || words.length === 0 || visualizerHash01(seed, 4, 107) > .62) return null;
		return {
			text: words[Math.floor(visualizerHash01(seed, 5, 109) * words.length) % words.length],
			x: .28 + visualizerHash01(seed, 6, 113) * .44,
			y: .26 + visualizerHash01(seed, 7, 127) * .48,
			rotation: (visualizerHash01(seed, 8, 131) - .5) * .5,
			scale: 2.6 + visualizerHash01(seed, 9, 137) * 1.9
		};
	};
	const buildDecorSpec = (paragraphKind, shotKind, seedKey, fragmentPool, watermarkPool, previousMotif) => {
		const seed = hashVisualizerSeed(seedKey);
		const motif = chooseWithoutRepeat$1(TEMPERA_DECOR_MOTIFS, seedKey, previousMotif);
		const sparse = resolveTemperaShotProfile(shotKind).mood === "quiet" || paragraphKind === "break" || paragraphKind === "outro";
		return {
			motif,
			hatchAngle: (visualizerHash01(seed, 1, 83) - .5) * (Math.PI / 2),
			crossCount: 1 + Math.floor(visualizerHash01(seed, 2, 89) * 3),
			scribbleSeed: mixVisualizerSeed(seed, 97),
			fragments: sparse ? buildDecorFragments(fragmentPool, 3 + Math.floor(visualizerHash01(seed, 3, 101) * 3), seed) : [],
			watermark: buildDecorWatermark(watermarkPool, seed, resolveTemperaShotProfile(shotKind).mood !== "loud")
		};
	};
	const resolveFlowAngle = (previous, seed) => {
		if (previous === null) return (visualizerHash01(seed, 6, 71) > .5 ? 1 : -1) * Math.PI / 2 + (visualizerHash01(seed, 8, 79) - .5) * .4;
		return previous + ((Math.sin(previous) >= 0 ? 1 : -1) * Math.PI / 2 - previous) * .3 + (visualizerHash01(seed, 7, 73) - .5) * .5;
	};
	const buildShots$1 = (lines, songLines, kind, paragraphIndex, seed, previousKind, previousMotif, previousFlow, wholeLineLyrics) => {
		let lastKind = previousKind;
		let lastMotif = previousMotif;
		let lastFlow = previousFlow;
		const paragraphEnd = lines.at(-1)?.renderEndTime ?? 0;
		const chunks = buildShotChunks(lines, seed, paragraphIndex, wholeLineLyrics);
		const byIndex = new Map(lines.map((line) => [line.sourceIndex, line]));
		return chunks.map((chunk, shotIndex) => {
			const sliceSegments = byIndex.get(chunk.lineIndex)?.segments.slice(chunk.segmentStart, chunk.segmentEnd) ?? [];
			const sliceText = sliceSegments.map((segment) => segment.text).join("");
			const wordCount = sliceSegments.filter((segment) => segment.isWordLike).length;
			const shotKind = chooseWithoutRepeat$1(resolveTemperaShotCandidates(kind === "breath" || kind !== "chorus" && wordCount <= 2 ? ["quiet"] : kind === "chorus" ? ["neutral", "loud"] : [
				"quiet",
				"neutral",
				"loud"
			]), `${seed}:${paragraphIndex}:${shotIndex}:${sliceText}`, lastKind);
			lastKind = shotKind;
			const cameraSeed = hashVisualizerSeed(`${seed}:${paragraphIndex}:${shotIndex}:camera`);
			const flowAngle = resolveFlowAngle(lastFlow, cameraSeed);
			lastFlow = flowAngle;
			const { start, end } = buildCameraKeys(shotKind, cameraSeed, flowAngle);
			const outsideSlice = lines.flatMap((item) => item.sourceIndex === chunk.lineIndex ? item.segments.filter((_, index) => index < chunk.segmentStart || index >= chunk.segmentEnd) : item.segments);
			const decorPool = outsideSlice.length > 0 ? outsideSlice : songLines.flatMap((item) => item.sourceIndex === chunk.lineIndex ? [] : item.segments);
			const fragmentPool = decorPool.map((segment) => segment.text).join("");
			const watermarkPool = decorPool.filter((segment) => segment.isWordLike).map((segment) => segment.text);
			const decor = buildDecorSpec(kind, shotKind, `${seed}:${paragraphIndex}:${shotIndex}:decor`, fragmentPool, watermarkPool, lastMotif);
			lastMotif = decor.motif;
			const slices = [{
				lineIndex: chunk.lineIndex,
				segmentStart: chunk.segmentStart,
				segmentEnd: chunk.segmentEnd
			}];
			return {
				id: `p${paragraphIndex}-s${shotIndex}`,
				kind: shotKind,
				startTime: chunk.startTime,
				endTime: shotIndex === chunks.length - 1 ? Math.max(chunk.endTime, paragraphEnd) : chunk.endTime,
				lyricEndTime: chunk.lyricEndTime,
				slices,
				isBridge: false,
				camera: start,
				cameraEnd: end,
				flowAngle,
				decor
			};
		});
	};
	/** Gaps shorter than this are already covered by the paragraph transition itself. */
	const BRIDGE_MIN_GAP = 1.2;
	const BRIDGE_MAX_LENGTH = 5;
	/**
	* Fills an instrumental gap with lyric-free shots. They run through the same hand-off, camera
	* and decor machinery as any other shot, so a long gap keeps moving instead of holding the
	* last sung composition - and the paragraph transition at the far end has something to carry.
	*/
	const buildBridgeShots = (paragraphKind, paragraphIndex, seed, fragmentPool, gapStart, gapEnd, previousKind, previousMotif, previousFlow) => {
		const gap = gapEnd - gapStart;
		if (gap < BRIDGE_MIN_GAP) return [];
		const count = Math.min(3, Math.max(1, Math.ceil(gap / BRIDGE_MAX_LENGTH)));
		const step = gap / count;
		let lastKind = previousKind;
		let lastMotif = previousMotif;
		let lastFlow = previousFlow;
		return Array.from({ length: count }, (_, index) => {
			const shotKind = chooseWithoutRepeat$1(resolveTemperaShotCandidates(["quiet", "neutral"]), `${seed}:${paragraphIndex}:bridge${index}`, lastKind);
			lastKind = shotKind;
			const cameraSeed = hashVisualizerSeed(`${seed}:${paragraphIndex}:bridge${index}:camera`);
			const flowAngle = resolveFlowAngle(lastFlow, cameraSeed);
			lastFlow = flowAngle;
			const { start, end } = buildCameraKeys(shotKind, cameraSeed, flowAngle);
			const decor = buildDecorSpec(paragraphKind, shotKind, `${seed}:${paragraphIndex}:bridge${index}:decor`, fragmentPool, [], lastMotif);
			lastMotif = decor.motif;
			return {
				id: `p${paragraphIndex}-b${index}`,
				kind: shotKind,
				startTime: gapStart + step * index,
				endTime: gapStart + step * (index + 1),
				lyricEndTime: gapStart + step * (index + 1),
				slices: [],
				isBridge: true,
				camera: start,
				cameraEnd: end,
				flowAngle,
				decor
			};
		});
	};
	const compileTemperaProgram = (lines, seed = "tempera", options = {}) => {
		const compiled = lines.map((line, sourceIndex) => ({
			sourceIndex,
			line,
			renderEndTime: Math.max(line.startTime, Math.min(getLineRenderEndTime(line), lines[sourceIndex + 1]?.startTime ?? Number.POSITIVE_INFINITY)),
			segments: buildTemperaSegments(line)
		}));
		const paragraphGapThreshold = resolveTemperaParagraphGapThreshold(lines);
		const drafts = [];
		let current = {
			lines: [],
			boundary: "song-start"
		};
		compiled.forEach((line, index) => {
			const previous = compiled[index - 1];
			const gap = previous ? line.line.startTime - previous.renderEndTime : 0;
			const boundary = previous && metadataChanged$1(previous.line, line.line) ? "metadata" : previous && gap >= paragraphGapThreshold ? "time-gap" : null;
			if (boundary && current.lines.length > 0) {
				drafts.push(...splitOversizedDraft$1(current));
				current = {
					lines: [],
					boundary
				};
			}
			current.lines.push(line);
		});
		if (current.lines.length > 0) drafts.push(...splitOversizedDraft$1(current));
		const resolvedSeed = String(seed);
		let previousShot = null;
		let previousMotif = null;
		let previousFlow = null;
		let previousTransition = null;
		const paragraphs = drafts.map((draft, index) => {
			const kind = classifyParagraph$1(draft.lines, index, drafts.length);
			const lyricShots = buildShots$1(draft.lines, compiled, kind, index, resolvedSeed, previousShot, previousMotif, previousFlow, options.wholeLineLyrics === true);
			previousShot = lyricShots.at(-1)?.kind ?? previousShot;
			previousMotif = lyricShots.at(-1)?.decor.motif ?? previousMotif;
			previousFlow = lyricShots.at(-1)?.flowAngle ?? previousFlow;
			const next = drafts[index + 1];
			const endTime = draft.lines.at(-1).renderEndTime;
			const gap = next ? next.lines[0].line.startTime - endTime : 0;
			const bridgeShots = next ? buildBridgeShots(kind, index, resolvedSeed, draft.lines.map((item) => item.line.fullText).join(""), endTime, next.lines[0].line.startTime, previousShot, previousMotif, previousFlow) : [];
			const shots = [...lyricShots, ...bridgeShots];
			previousShot = shots.at(-1)?.kind ?? previousShot;
			previousMotif = shots.at(-1)?.decor.motif ?? previousMotif;
			previousFlow = shots.at(-1)?.flowAngle ?? previousFlow;
			const transitionKind = next ? chooseWithoutRepeat$1(TEMPERA_TRANSITION_KINDS, `${resolvedSeed}:${index}:transition`, previousTransition) : null;
			if (transitionKind) previousTransition = transitionKind;
			const transitionDuration = next ? Math.min(1, Math.max(.35, Math.max(gap, 0) + .3)) : 0;
			const transitionEndTime = next?.lines[0].line.startTime ?? endTime;
			return {
				id: `tempera-p${index}`,
				kind,
				boundary: draft.boundary,
				startTime: draft.lines[0].line.startTime,
				endTime,
				lines: draft.lines,
				shots,
				transitionOut: transitionKind ? {
					kind: transitionKind,
					startTime: Math.max(draft.lines[0].line.startTime, transitionEndTime - transitionDuration),
					endTime: transitionEndTime
				} : null
			};
		});
		paragraphs.forEach((paragraph, index) => {
			const incoming = paragraph.shots[0];
			const transition = paragraphs[index - 1]?.transitionOut;
			if (!incoming || !transition || transition.kind === "block-wipe") return;
			incoming.startTime = Math.min(incoming.startTime, Math.max(transition.startTime, paragraphs[index - 1].endTime));
		});
		return {
			version: 1,
			seed: resolvedSeed,
			paragraphGapThreshold,
			paragraphs
		};
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/tempera/temperaHatch.ts
	const TAU$1 = Math.PI * 2;
	const MAX_HATCH_LINES = 320;
	const buildHatchSpec = (seed, salt, scale = 1) => {
		const angleIndex = Math.floor(visualizerHash01(seed, 1, salt) * 4);
		const angle = [
			-Math.PI / 4,
			Math.PI / 4,
			-Math.PI / 3,
			Math.PI / 6
		][angleIndex] ?? Math.PI / 4;
		const spacing = ([
			9,
			13,
			18
		][Math.floor(visualizerHash01(seed, 2, salt) * 3)] ?? 13) * scale;
		return {
			angle,
			spacing,
			width: Math.max(.8, spacing * (.16 + visualizerHash01(seed, 3, salt) * .14))
		};
	};
	const rectPolygon = (x, y, width, height) => [
		x,
		y,
		x + width,
		y,
		x + width,
		y + height,
		x,
		y + height
	];
	const circlePolygon = (cx, cy, radius, segments = 28) => {
		const points = [];
		const count = Math.max(6, Math.round(segments));
		for (let index = 0; index < count; index += 1) {
			const angle = index / count * TAU$1;
			points.push(cx + Math.cos(angle) * radius, cy + Math.sin(angle) * radius);
		}
		return points;
	};
	const diamondPolygon = (cx, cy, rx, ry) => [
		cx,
		cy - ry,
		cx + rx,
		cy,
		cx,
		cy + ry,
		cx - rx,
		cy
	];
	const clipToConvex = (polygon, ax, ay, dx, dy, span) => {
		const count = polygon.length / 2;
		let cx = 0;
		let cy = 0;
		for (let index = 0; index < count; index += 1) {
			cx += polygon[index * 2];
			cy += polygon[index * 2 + 1];
		}
		cx /= count;
		cy /= count;
		let tMin = -span;
		let tMax = span;
		for (let index = 0; index < count; index += 1) {
			const x0 = polygon[index * 2];
			const y0 = polygon[index * 2 + 1];
			const x1 = polygon[(index + 1) % count * 2];
			let nx = polygon[(index + 1) % count * 2 + 1] - y0;
			let ny = -(x1 - x0);
			if (nx * (cx - x0) + ny * (cy - y0) > 0) {
				nx = -nx;
				ny = -ny;
			}
			const denominator = nx * dx + ny * dy;
			const numerator = nx * (ax - x0) + ny * (ay - y0);
			if (Math.abs(denominator) < 1e-9) {
				if (numerator > 0) return null;
				continue;
			}
			const t = -numerator / denominator;
			if (denominator > 0) tMax = Math.min(tMax, t);
			else tMin = Math.max(tMin, t);
			if (tMin > tMax) return null;
		}
		if (tMax - tMin < .5) return null;
		return {
			x1: ax + dx * tMin,
			y1: ay + dy * tMin,
			x2: ax + dx * tMax,
			y2: ay + dy * tMax
		};
	};
	const buildHatchLines = (polygon, spec, coverage = 1) => {
		if (polygon.length < 6 || spec.spacing <= 0) return [];
		const xs = [];
		const ys = [];
		for (let index = 0; index < polygon.length; index += 2) {
			xs.push(polygon[index]);
			ys.push(polygon[index + 1]);
		}
		const minX = Math.min(...xs);
		const maxX = Math.max(...xs);
		const minY = Math.min(...ys);
		const maxY = Math.max(...ys);
		const centerX = (minX + maxX) / 2;
		const centerY = (minY + maxY) / 2;
		const diagonal = Math.hypot(maxX - minX, maxY - minY);
		if (diagonal <= 0) return [];
		const dx = Math.cos(spec.angle);
		const dy = Math.sin(spec.angle);
		const px = -dy;
		const py = dx;
		const steps = Math.min(MAX_HATCH_LINES / 2, Math.ceil(diagonal / (spec.spacing * 2)) + 2);
		const clampedCoverage = Math.min(1, Math.max(0, coverage));
		const lines = [];
		for (let step = -steps; step <= steps; step += 1) {
			const offset = step * spec.spacing;
			const clipped = clipToConvex(polygon, centerX + px * offset, centerY + py * offset, dx, dy, diagonal);
			if (!clipped) continue;
			if (clampedCoverage >= 1) {
				lines.push(clipped);
				continue;
			}
			const midX = (clipped.x1 + clipped.x2) / 2;
			const midY = (clipped.y1 + clipped.y2) / 2;
			lines.push({
				x1: midX + (clipped.x1 - midX) * clampedCoverage,
				y1: midY + (clipped.y1 - midY) * clampedCoverage,
				x2: midX + (clipped.x2 - midX) * clampedCoverage,
				y2: midY + (clipped.y2 - midY) * clampedCoverage
			});
		}
		return lines;
	};
	const buildScribblePath = (seed, salt, cx, cy, radius, turns = 2) => {
		const perTurn = 13;
		const total = Math.max(perTurn, Math.round(perTurn * Math.max(1, turns)));
		const points = [];
		for (let index = 0; index <= total; index += 1) {
			const progress = index / total;
			const angle = progress * TAU$1 * Math.max(1, turns);
			const jitterR = (visualizerHash01(seed, index, salt) - .5) * radius * .26;
			const jitterA = (visualizerHash01(seed, index, salt + 7) - .5) * .22;
			const currentRadius = radius * (.52 + .48 * progress) + jitterR;
			points.push(cx + Math.cos(angle + jitterA) * currentRadius, cy + Math.sin(angle + jitterA) * currentRadius * .82);
		}
		return points;
	};
	const buildWavyPath = (seed, salt, x0, x1, y, amplitude, steps = 24) => {
		const points = [];
		const safeSteps = Math.max(2, Math.round(steps));
		for (let index = 0; index <= safeSteps; index += 1) {
			const progress = index / safeSteps;
			const wave = Math.sin(progress * TAU$1 * 1.5 + visualizerHash01(seed, 0, salt) * TAU$1);
			const jitter = (visualizerHash01(seed, index, salt) - .5) * amplitude * .5;
			points.push(x0 + (x1 - x0) * progress, y + wave * amplitude + jitter);
		}
		return points;
	};
	const buildMarkRow = (seed, salt, x, y, count, spacing, size, angle) => {
		const marks = [];
		const total = Math.max(0, Math.round(count));
		const dx = Math.cos(angle);
		const dy = Math.sin(angle);
		for (let index = 0; index < total; index += 1) {
			const jitter = (visualizerHash01(seed, index, salt) - .5) * spacing * .16;
			const distance = index * spacing + jitter;
			marks.push({
				x: x + dx * distance,
				y: y + dy * distance,
				size: size * (.82 + visualizerHash01(seed, index, salt + 3) * .36),
				rotation: (visualizerHash01(seed, index, salt + 11) - .5) * .3
			});
		}
		return marks;
	};
	const buildCrossRow = (seed, salt, x, y, count, spacing, size = 9, angle = 0) => buildMarkRow(seed, salt, x, y, count, spacing, size, angle);
	const buildDotRow = (seed, salt, x, y, count, spacing, size = 4, angle = Math.PI / 2) => buildMarkRow(seed, salt, x, y, count, spacing, size, angle);
	const buildDotGrid = (width, height, spacing, size = 1.6) => {
		const marks = [];
		if (spacing <= 0 || width <= 0 || height <= 0) return marks;
		const columns = Math.min(200, Math.ceil(width / spacing) + 1);
		const rows = Math.min(200, Math.ceil(height / spacing) + 1);
		for (let row = 0; row < rows; row += 1) for (let column = 0; column < columns; column += 1) marks.push({
			x: column * spacing + (row % 2 === 0 ? 0 : spacing / 2),
			y: row * spacing,
			size,
			rotation: 0
		});
		return marks;
	};
	const buildCrossingLines = (seed, salt, width, height, count) => {
		const total = Math.min(3, Math.max(0, Math.round(count)));
		const lines = [];
		for (let index = 0; index < total; index += 1) {
			const anchorY = height * (.18 + visualizerHash01(seed, index, salt) * .64);
			const angle = (visualizerHash01(seed, index, salt + 5) > .5 ? 1 : -1) * (.07 + visualizerHash01(seed, index, salt + 9) * .11);
			const reach = width * .9;
			lines.push({
				x1: -width * .3,
				y1: anchorY - Math.tan(angle) * reach,
				x2: width * 1.3,
				y2: anchorY + Math.tan(angle) * reach
			});
		}
		return lines;
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/tempera/temperaShapes.ts
	const toPixiColor = (pixi, color) => pixi.Color.shared.setValue(color).toNumber();
	const boundsCenter = (polygon) => {
		let minX = Number.POSITIVE_INFINITY;
		let maxX = Number.NEGATIVE_INFINITY;
		let minY = Number.POSITIVE_INFINITY;
		let maxY = Number.NEGATIVE_INFINITY;
		for (let index = 0; index < polygon.length; index += 2) {
			minX = Math.min(minX, polygon[index]);
			maxX = Math.max(maxX, polygon[index]);
			minY = Math.min(minY, polygon[index + 1]);
			maxY = Math.max(maxY, polygon[index + 1]);
		}
		return {
			x: (minX + maxX) / 2,
			y: (minY + maxY) / 2
		};
	};
	const gradientFills = /* @__PURE__ */ new WeakMap();
	const buildGradientFill = (pixi, gradient, color) => {
		let byColor = gradientFills.get(gradient);
		if (!byColor) {
			byColor = /* @__PURE__ */ new Map();
			gradientFills.set(gradient, byColor);
		}
		let fill = byColor.get(color);
		if (!fill) {
			fill = createGradientFill(pixi, gradient, color);
			byColor.set(color, fill);
		}
		return fill;
	};
	/**
	* In gradient mode a shape is filled with the whole four-colour ramp instead of one tone.
	* Each stop is pulled halfway toward the tone the composition asked for, so the ramp carries
	* the cover's hues while the shape keeps the brightness its place in the composition needs.
	*/
	const createGradientFill = (pixi, gradient, color) => {
		const half = .5;
		const dx = Math.cos(gradient.angle) * half;
		const dy = Math.sin(gradient.angle) * half;
		const stops = gradient.colors.map((stop, index) => ({
			offset: gradient.colors.length > 1 ? index / (gradient.colors.length - 1) : 0,
			color: mixColors(stop, color, .5)
		}));
		return new pixi.FillGradient({
			type: "linear",
			start: {
				x: half - dx,
				y: half - dy
			},
			end: {
				x: half + dx,
				y: half + dy
			},
			colorStops: stops,
			textureSpace: "local"
		});
	};
	const drawPolygonFill = (pixi, polygon, color, alpha = 1, gradient) => {
		const node = new pixi.Graphics().poly(polygon);
		return gradient && gradient.colors.length > 1 ? node.fill({
			fill: buildGradientFill(pixi, gradient, color),
			alpha
		}) : node.fill({
			color: toPixiColor(pixi, color),
			alpha
		});
	};
	/**
	* A filled polygon with holes punched through it. The holes are genuinely transparent: the
	* Pixi canvas runs on `backgroundAlpha: 0`, so what shows through is the shell's live
	* background layer (veiled by the scene's paper wash, which is per-paragraph and cannot be
	* cut per shot).
	*
	* The node must hold exactly one fill instruction. `GraphicsContext.cut` walks back over the
	* last two instructions and, once the first hole is attached, keeps going - a second fill or
	* a stroke on the same node would silently collect the holes as well. Outlines for the holes
	* are therefore separate nodes.
	*/
	const drawPolygonFillWithHoles = (pixi, polygon, holes, color, alpha = 1, gradient) => {
		const node = new pixi.Graphics().poly(polygon);
		if (gradient && gradient.colors.length > 1) node.fill({
			fill: buildGradientFill(pixi, gradient, color),
			alpha
		});
		else node.fill({
			color: toPixiColor(pixi, color),
			alpha
		});
		holes.forEach((hole) => {
			if (hole.length >= 6) node.poly(hole).cut();
		});
		return node;
	};
	const drawPolygonOutline = (pixi, polygon, color, width, alpha = 1) => new pixi.Graphics().poly(polygon).stroke({
		color: toPixiColor(pixi, color),
		width,
		alpha
	});
	const drawHatchFill = (pixi, polygon, spec, color, alpha = 1) => {
		const node = new pixi.Graphics();
		const lines = buildHatchLines(polygon, spec);
		lines.forEach((line) => {
			node.moveTo(line.x1, line.y1).lineTo(line.x2, line.y2);
		});
		if (lines.length > 0) node.stroke({
			color: toPixiColor(pixi, color),
			width: spec.width,
			alpha
		});
		const center = boundsCenter(polygon);
		node.pivot.set(center.x, center.y);
		node.position.set(center.x, center.y);
		return node;
	};
	const discsCenter = (discs) => {
		if (discs.length === 0) return {
			x: 0,
			y: 0
		};
		let minX = Number.POSITIVE_INFINITY;
		let maxX = Number.NEGATIVE_INFINITY;
		let minY = Number.POSITIVE_INFINITY;
		let maxY = Number.NEGATIVE_INFINITY;
		discs.forEach((disc) => {
			minX = Math.min(minX, disc.x - disc.radius);
			maxX = Math.max(maxX, disc.x + disc.radius);
			minY = Math.min(minY, disc.y - disc.radius);
			maxY = Math.max(maxY, disc.y + disc.radius);
		});
		return {
			x: (minX + maxX) / 2,
			y: (minY + maxY) / 2
		};
	};
	const buildDiscNode = (pixi, discs) => {
		const node = new pixi.Graphics();
		discs.forEach((disc) => {
			node.circle(disc.x, disc.y, Math.max(.5, disc.radius));
		});
		return node;
	};
	const pivotOnSelf = (node, center) => {
		node.pivot.set(center.x, center.y);
		node.position.set(center.x, center.y);
		return node;
	};
	const drawDiscs = (pixi, discs, color, alpha = 1, gradient) => {
		const node = buildDiscNode(pixi, discs);
		if (discs.length > 0) node.fill(gradient && gradient.colors.length > 1 ? {
			fill: buildGradientFill(pixi, gradient, color),
			alpha
		} : {
			color: toPixiColor(pixi, color),
			alpha
		});
		return pivotOnSelf(node, discsCenter(discs));
	};
	const drawRings = (pixi, discs, color, width, alpha = 1) => {
		const node = buildDiscNode(pixi, discs);
		if (discs.length > 0) node.stroke({
			color: toPixiColor(pixi, color),
			width,
			alpha
		});
		return pivotOnSelf(node, discsCenter(discs));
	};
	const drawLines = (pixi, lines, color, width, alpha = 1) => {
		const node = new pixi.Graphics();
		lines.forEach((line) => {
			node.moveTo(line.x1, line.y1).lineTo(line.x2, line.y2);
		});
		if (lines.length > 0) node.stroke({
			color: toPixiColor(pixi, color),
			width,
			alpha
		});
		return node;
	};
	const drawPolyline = (pixi, points, color, width, alpha = 1) => {
		const node = new pixi.Graphics();
		if (points.length < 4) return node;
		node.moveTo(points[0], points[1]);
		for (let index = 2; index < points.length; index += 2) node.lineTo(points[index], points[index + 1]);
		return node.stroke({
			color: toPixiColor(pixi, color),
			width,
			alpha
		});
	};
	const drawConcentricDiamonds = (pixi, cx, cy, rx, ry, rings, color, alpha = 1) => {
		const node = new pixi.Graphics();
		const stroke = toPixiColor(pixi, color);
		for (let ring = 0; ring < Math.max(1, rings); ring += 1) {
			const shrink = 1 - ring * .22;
			node.poly([
				cx,
				cy - ry * shrink,
				cx + rx * shrink,
				cy,
				cx,
				cy + ry * shrink,
				cx - rx * shrink,
				cy
			]).stroke({
				color: stroke,
				width: ring % 2 === 0 ? 3.5 : 1.4,
				alpha
			});
		}
		node.pivot.set(cx, cy);
		node.position.set(cx, cy);
		return node;
	};
	const drawCrossMarks = (pixi, marks, color, width, alpha = 1) => {
		const node = new pixi.Graphics();
		marks.forEach((mark) => {
			const cos = Math.cos(mark.rotation + Math.PI / 4) * mark.size;
			const sin = Math.sin(mark.rotation + Math.PI / 4) * mark.size;
			node.moveTo(mark.x - cos, mark.y - sin).lineTo(mark.x + cos, mark.y + sin);
			node.moveTo(mark.x - sin, mark.y + cos).lineTo(mark.x + sin, mark.y - cos);
		});
		if (marks.length > 0) node.stroke({
			color: toPixiColor(pixi, color),
			width,
			alpha
		});
		return node;
	};
	const drawSquareMarks = (pixi, marks, color, alpha = 1) => {
		const node = new pixi.Graphics();
		marks.forEach((mark) => {
			node.rect(mark.x - mark.size / 2, mark.y - mark.size / 2, mark.size, mark.size);
		});
		if (marks.length > 0) node.fill({
			color: toPixiColor(pixi, color),
			alpha
		});
		return node;
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/tempera/compositions/temperaSplitCompositions.ts
	const addPanels = (ctx, panels, hatchIndex) => {
		const hatch = buildHatchSpec(ctx.seed, 5);
		panels.forEach((panel, index) => {
			ctx.add(drawPolygonFill(ctx.pixi, panel.polygon, panel.tone, .96, ctx.gradient), {
				delay: index * .05,
				span: .5,
				enterDX: panel.enterDX,
				enterDY: panel.enterDY
			});
			if (index !== hatchIndex) return;
			ctx.add(drawHatchFill(ctx.pixi, panel.polygon, hatch, ctx.palette.tone4, .5), {
				delay: index * .05 + .06,
				span: .5,
				grow: true
			});
		});
	};
	const addSeam = (ctx, polygon, delay) => {
		ctx.add(drawPolygonFill(ctx.pixi, polygon, ctx.palette.ink, .85, ctx.gradient), {
			delay,
			span: .5
		});
	};
	const duoSplit = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const horizontal = visualizerHash01(ctx.seed, 1, 3) > .5;
		addPanels(ctx, horizontal ? [{
			polygon: rectPolygon(-bleed, -bleed, width + bleed * 2, height * .52 + bleed),
			tone: palette.tone1,
			enterDX: 0,
			enterDY: -height * .55
		}, {
			polygon: rectPolygon(-bleed, height * .52, width + bleed * 2, height * .48 + bleed),
			tone: palette.tone3,
			enterDX: 0,
			enterDY: height * .55
		}] : [{
			polygon: rectPolygon(-bleed, -bleed, width * .5 + bleed, height + bleed * 2),
			tone: palette.tone1,
			enterDX: -width * .5,
			enterDY: 0
		}, {
			polygon: rectPolygon(width * .5, -bleed, width * .5 + bleed, height + bleed * 2),
			tone: palette.tone3,
			enterDX: width * .5,
			enterDY: 0
		}], 0);
		addSeam(ctx, horizontal ? rectPolygon(-bleed, height * .52 - 1.5, width + bleed * 2, 3) : rectPolygon(width * .5 - 1.5, -bleed, 3, height + bleed * 2), .16);
	};
	const quadSplit = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const splitX = width * (.42 + visualizerHash01(ctx.seed, 2, 7) * .16);
		const splitY = height * (.42 + visualizerHash01(ctx.seed, 3, 11) * .16);
		addPanels(ctx, [
			{
				polygon: rectPolygon(-bleed, -bleed, splitX + bleed, splitY + bleed),
				tone: palette.tone1,
				enterDX: -width * .3,
				enterDY: -height * .3
			},
			{
				polygon: rectPolygon(splitX, -bleed, width - splitX + bleed, splitY + bleed),
				tone: palette.tone4,
				enterDX: width * .3,
				enterDY: -height * .3
			},
			{
				polygon: rectPolygon(-bleed, splitY, splitX + bleed, height - splitY + bleed),
				tone: palette.tone3,
				enterDX: -width * .3,
				enterDY: height * .3
			},
			{
				polygon: rectPolygon(splitX, splitY, width - splitX + bleed, height - splitY + bleed),
				tone: palette.tone2,
				enterDX: width * .3,
				enterDY: height * .3
			}
		], 3);
		addSeam(ctx, rectPolygon(splitX - 1.5, -bleed, 3, height + bleed * 2), .2);
		addSeam(ctx, rectPolygon(-bleed, splitY - 1.5, width + bleed * 2, 3), .24);
	};
	const triColumn = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const edge = width * (.26 + visualizerHash01(ctx.seed, 4, 13) * .06);
		addPanels(ctx, [
			{
				polygon: rectPolygon(-bleed, -bleed, edge + bleed, height + bleed * 2),
				tone: palette.tone1,
				enterDX: -width * .25,
				enterDY: 0
			},
			{
				polygon: rectPolygon(edge, -bleed, width - edge * 2, height + bleed * 2),
				tone: palette.tone4,
				enterDX: 0,
				enterDY: -height * .3
			},
			{
				polygon: rectPolygon(width - edge, -bleed, edge + bleed, height + bleed * 2),
				tone: palette.tone1,
				enterDX: width * .25,
				enterDY: 0
			}
		], 0);
	};
	const thirdsStack = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const band = height * .34;
		addPanels(ctx, [
			{
				polygon: rectPolygon(-bleed, -bleed, width + bleed * 2, band + bleed),
				tone: palette.tone2,
				enterDX: -width * .2,
				enterDY: 0
			},
			{
				polygon: rectPolygon(-bleed, band, width + bleed * 2, band),
				tone: palette.tone4,
				enterDX: width * .2,
				enterDY: 0
			},
			{
				polygon: rectPolygon(-bleed, band * 2, width + bleed * 2, height - band * 2 + bleed),
				tone: palette.tone1,
				enterDX: -width * .2,
				enterDY: 0
			}
		], 0);
	};
	const checkerQuad = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const splitX = width * .5;
		const splitY = height * .5;
		addPanels(ctx, [
			{
				polygon: rectPolygon(-bleed, -bleed, splitX + bleed, splitY + bleed),
				tone: palette.tone4,
				enterDX: 0,
				enterDY: -height * .35
			},
			{
				polygon: rectPolygon(splitX, -bleed, width - splitX + bleed, splitY + bleed),
				tone: palette.tone1,
				enterDX: 0,
				enterDY: -height * .35
			},
			{
				polygon: rectPolygon(-bleed, splitY, splitX + bleed, height - splitY + bleed),
				tone: palette.tone1,
				enterDX: 0,
				enterDY: height * .35
			},
			{
				polygon: rectPolygon(splitX, splitY, width - splitX + bleed, height - splitY + bleed),
				tone: palette.tone4,
				enterDX: 0,
				enterDY: height * .35
			}
		], 1);
	};
	const cornerWedge = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const fromLeft = visualizerHash01(ctx.seed, 5, 17) > .5;
		const apexX = fromLeft ? width * .78 : width * .22;
		addPanels(ctx, [{
			polygon: rectPolygon(-bleed, -bleed, width + bleed * 2, height + bleed * 2),
			tone: palette.tone1,
			enterDX: 0,
			enterDY: 0
		}, {
			polygon: fromLeft ? [
				-bleed,
				-bleed,
				apexX,
				-bleed,
				-bleed,
				height + bleed
			] : [
				width + bleed,
				-bleed,
				apexX,
				-bleed,
				width + bleed,
				height + bleed
			],
			tone: palette.tone4,
			enterDX: fromLeft ? -width * .4 : width * .4,
			enterDY: 0
		}], 1);
	};
	const diagonalHalves = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const lean = height * (.2 + visualizerHash01(ctx.seed, 6, 19) * .3);
		addPanels(ctx, [{
			polygon: [
				-bleed,
				-bleed,
				width + bleed,
				-bleed,
				width + bleed,
				lean,
				-bleed,
				height - lean
			],
			tone: palette.tone2,
			enterDX: 0,
			enterDY: -height * .4
		}, {
			polygon: [
				-bleed,
				height - lean,
				width + bleed,
				lean,
				width + bleed,
				height + bleed,
				-bleed,
				height + bleed
			],
			tone: palette.tone4,
			enterDX: 0,
			enterDY: height * .4
		}], 1);
	};
	const crossAxis = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const barX = width * .11;
		const barY = height * .13;
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, height + bleed * 2), palette.tone1, .9, ctx.gradient), { span: .5 });
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon((width - barX) / 2, -bleed, barX, height + bleed * 2), palette.tone4, .95), {
			delay: .06,
			span: .5,
			enterDY: -height * .5
		});
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, (height - barY) / 2, width + bleed * 2, barY), palette.tone3, .95), {
			delay: .12,
			span: .5,
			enterDX: width * .5
		});
	};
	const offsetHalves = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const step = height * (.06 + visualizerHash01(ctx.seed, 7, 23) * .06);
		const seam = width * .48;
		addPanels(ctx, [
			{
				polygon: rectPolygon(-bleed, -bleed, seam + bleed, height * .5 + step + bleed),
				tone: palette.tone1,
				enterDX: -width * .3,
				enterDY: 0
			},
			{
				polygon: rectPolygon(seam, -bleed, width - seam + bleed, height * .5 - step + bleed),
				tone: palette.tone3,
				enterDX: width * .3,
				enterDY: 0
			},
			{
				polygon: rectPolygon(-bleed, height * .5 + step, seam + bleed, height * .5 - step + bleed),
				tone: palette.tone4,
				enterDX: -width * .2,
				enterDY: 0
			},
			{
				polygon: rectPolygon(seam, height * .5 - step, width - seam + bleed, height * .5 + step + bleed),
				tone: palette.tone2,
				enterDX: width * .2,
				enterDY: 0
			}
		], 2);
	};
	const stairBlocks = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const tones = [
			palette.tone1,
			palette.tone2,
			palette.tone3,
			palette.tone4
		];
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, height + bleed * 2), palette.tone1, .9, ctx.gradient), { span: .5 });
		tones.forEach((tone, index) => {
			const left = width * (.06 + index * .2);
			const top = height * (.1 + index * .16);
			ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(left, top, width * .3, height * .34), tone, .94, ctx.gradient), {
				delay: index * .06,
				span: .5,
				enterDX: -width * .2,
				enterDY: height * .12
			});
		});
	};
	const pillarGap = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const side = (width - width * (.26 + visualizerHash01(ctx.seed, 8, 29) * .08)) / 2;
		addPanels(ctx, [{
			polygon: rectPolygon(-bleed, -bleed, side + bleed, height + bleed * 2),
			tone: palette.tone4,
			enterDX: -width * .3,
			enterDY: 0
		}, {
			polygon: rectPolygon(width - side, -bleed, side + bleed, height + bleed * 2),
			tone: palette.tone4,
			enterDX: width * .3,
			enterDY: 0
		}], 1);
	};
	const cornerQuad = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const splitX = width * (.6 + visualizerHash01(ctx.seed, 9, 31) * .12);
		const splitY = height * (.62 + visualizerHash01(ctx.seed, 10, 37) * .1);
		addPanels(ctx, [
			{
				polygon: rectPolygon(-bleed, -bleed, splitX + bleed, splitY + bleed),
				tone: palette.tone2,
				enterDX: 0,
				enterDY: -height * .25
			},
			{
				polygon: rectPolygon(splitX, -bleed, width - splitX + bleed, splitY + bleed),
				tone: palette.tone4,
				enterDX: width * .25,
				enterDY: 0
			},
			{
				polygon: rectPolygon(-bleed, splitY, splitX + bleed, height - splitY + bleed),
				tone: palette.tone1,
				enterDX: -width * .25,
				enterDY: 0
			},
			{
				polygon: rectPolygon(splitX, splitY, width - splitX + bleed, height - splitY + bleed),
				tone: palette.tone3,
				enterDX: 0,
				enterDY: height * .25
			}
		], 0);
	};
	const sliverStack = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const fromLeft = visualizerHash01(ctx.seed, 11, 41) > .5;
		const slabWidth = width * .46;
		const left = fromLeft ? -bleed : width - slabWidth;
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, height + bleed * 2), palette.tone1, .9, ctx.gradient), { span: .5 });
		const count = 9;
		const sliver = (height + bleed * 2) / count;
		for (let index = 0; index < count; index += 1) {
			if (index % 2 === 1) continue;
			ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(left, -bleed + sliver * index, slabWidth + bleed, sliver), palette.tone4, .92, ctx.gradient), {
				delay: index * .02,
				span: .5,
				enterDX: (fromLeft ? -1 : 1) * width * .2
			});
		}
	};
	const TEMPERA_SPLIT_COMPOSITIONS = {
		"duo-split": duoSplit,
		"quad-split": quadSplit,
		"tri-column": triColumn,
		"thirds-stack": thirdsStack,
		"checker-quad": checkerQuad,
		"corner-wedge": cornerWedge,
		"diagonal-halves": diagonalHalves,
		"cross-axis": crossAxis,
		"offset-halves": offsetHalves,
		"stair-blocks": stairBlocks,
		"pillar-gap": pillarGap,
		"corner-quad": cornerQuad,
		"sliver-stack": sliverStack
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/tempera/compositions/temperaBandCompositions.ts
	const bandStrip = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const bandY = height * .37;
		const bandHeight = height * .3;
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, bandY, width + bleed * 2, bandHeight), palette.tone3, .96, ctx.gradient), {
			span: .55,
			enterDX: -width * .5
		});
		ctx.add(drawLines(ctx.pixi, [{
			x1: -bleed,
			y1: bandY - 10,
			x2: width + bleed,
			y2: bandY - 22
		}, {
			x1: -bleed,
			y1: bandY + bandHeight + 22,
			x2: width + bleed,
			y2: bandY + bandHeight + 10
		}], palette.tone4, 1.4, .75), {
			delay: .12,
			enterDX: width * .25
		});
		if (!ctx.showDecor) return;
		ctx.add(drawCrossMarks(ctx.pixi, buildCrossRow(ctx.seed, 17, width * .06, bandY - height * .16, 4, width * .055, 9), palette.ink, 2, .8), {
			delay: .2,
			span: .5
		});
		ctx.add(drawSquareMarks(ctx.pixi, buildDotRow(ctx.seed, 19, width * .94, bandY + bandHeight + height * .06, 3, height * .05, 6), palette.ink, .75), {
			delay: .26,
			drift: true
		});
	};
	const horizonBand = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const waterline = height * (.5 + visualizerHash01(ctx.seed, 1, 23) * .12);
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, waterline + bleed), palette.tone1, .94, ctx.gradient), {
			span: .55,
			enterDY: -height * .3
		});
		const water = rectPolygon(-bleed, waterline, width + bleed * 2, height - waterline + bleed);
		ctx.add(drawPolygonFill(ctx.pixi, water, palette.tone3, .95, ctx.gradient), {
			delay: .05,
			span: .55,
			enterDY: height * .3
		});
		ctx.add(drawHatchFill(ctx.pixi, water, {
			...buildHatchSpec(ctx.seed, 29),
			angle: 0
		}, palette.tone4, .55), {
			delay: .1,
			span: .55,
			grow: true
		});
		ctx.add(drawPolyline(ctx.pixi, buildWavyPath(ctx.seed, 31, -bleed, width + bleed, waterline, height * .012, 30), palette.ink, 2.2, .85), {
			delay: .16,
			span: .5
		});
	};
	const deepDive = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const tones = [
			palette.tone1,
			palette.tone2,
			palette.tone3,
			palette.tone4
		];
		const bandHeight = (height + bleed * 2) / tones.length;
		tones.forEach((tone, index) => {
			const top = -bleed + bandHeight * index;
			ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, top, width + bleed * 2, bandHeight + 1), tone, .95, ctx.gradient), {
				delay: index * .06,
				span: .55,
				enterDY: height * .3
			});
			if (index === 0) return;
			ctx.add(drawPolyline(ctx.pixi, buildWavyPath(ctx.seed, 37 + index, -bleed, width + bleed, top, height * .008, 24), palette.paper, 1.6, .5), {
				delay: index * .06 + .05,
				span: .5
			});
		});
	};
	const toneRamp = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const spec = buildHatchSpec(ctx.seed, 41);
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, height + bleed * 2), palette.tone1, .92, ctx.gradient), { span: .5 });
		const steps = 4;
		for (let index = 0; index < steps; index += 1) {
			const columnWidth = (width + bleed * 2) / steps;
			const column = rectPolygon(-bleed + columnWidth * index, -bleed, columnWidth, height + bleed * 2);
			ctx.add(drawHatchFill(ctx.pixi, column, {
				...spec,
				spacing: spec.spacing * (1.6 - index * .32)
			}, palette.tone4, .6), {
				delay: index * .06,
				span: .55,
				grow: true
			});
		}
	};
	const doubleBand = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const slot = height * (.2 + visualizerHash01(ctx.seed, 51, 43) * .08);
		const rail = height * .3;
		const top = (height - slot) / 2 - rail;
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, top, width + bleed * 2, rail), palette.tone3, .95, ctx.gradient), {
			span: .55,
			enterDX: -width * .4
		});
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, top + rail + slot, width + bleed * 2, rail), palette.tone4, .95, ctx.gradient), {
			delay: .07,
			span: .55,
			enterDX: width * .4
		});
	};
	const tiltBand = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const lean = height * (.14 + visualizerHash01(ctx.seed, 53, 47) * .14);
		const half = height * .17;
		const band = [
			-bleed,
			height / 2 + lean - half,
			width + bleed,
			height / 2 - lean - half,
			width + bleed,
			height / 2 - lean + half,
			-bleed,
			height / 2 + lean + half
		];
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, height + bleed * 2), palette.tone1, .9, ctx.gradient), { span: .5 });
		ctx.add(drawPolygonFill(ctx.pixi, band, palette.tone4, .95, ctx.gradient), {
			delay: .06,
			span: .55,
			enterDX: -width * .35
		});
		ctx.add(drawHatchFill(ctx.pixi, band, {
			...buildHatchSpec(ctx.seed, 59),
			angle: Math.PI / 2
		}, palette.paper, .3), {
			delay: .12,
			span: .55,
			grow: true
		});
	};
	const edgeRails = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const rail = height * (.16 + visualizerHash01(ctx.seed, 61, 53) * .06);
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, rail + bleed), palette.tone3, .94, ctx.gradient), {
			span: .55,
			enterDY: -height * .2
		});
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, height - rail, width + bleed * 2, rail + bleed), palette.tone3, .94, ctx.gradient), {
			delay: .06,
			span: .55,
			enterDY: height * .2
		});
		if (!ctx.showDecor) return;
		ctx.add(drawLines(ctx.pixi, [{
			x1: -bleed,
			y1: rail + 8,
			x2: width + bleed,
			y2: rail + 8
		}, {
			x1: -bleed,
			y1: height - rail - 8,
			x2: width + bleed,
			y2: height - rail - 8
		}], palette.tone4, 1.2, .6), {
			delay: .16,
			span: .5,
			enterDX: width * .2
		});
	};
	const gradientWall = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const spec = buildHatchSpec(ctx.seed, 67);
		const downward = visualizerHash01(ctx.seed, 71, 59) > .5;
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, height + bleed * 2), palette.tone2, .94, ctx.gradient), { span: .5 });
		const steps = 5;
		const bandHeight = height / steps;
		for (let index = 0; index < steps; index += 1) {
			const rank = downward ? index : 4 - index;
			const top = bandHeight * index - (index === 0 ? bleed : 0);
			const bottom = bandHeight * (index + 1) + (index === 4 ? bleed : 0);
			ctx.add(drawHatchFill(ctx.pixi, rectPolygon(-bleed, top, width + bleed * 2, bottom - top), {
				...spec,
				spacing: spec.spacing * (1.9 - rank * .34)
			}, palette.tone4, .55), {
				delay: index * .05,
				span: .55,
				grow: true
			});
		}
	};
	const terrace = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const tones = [
			palette.tone1,
			palette.tone2,
			palette.tone3,
			palette.tone4
		];
		const bandHeight = height / tones.length;
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, height + bleed * 2), palette.tone1, .9, ctx.gradient), { span: .5 });
		tones.forEach((tone, index) => {
			const left = width * index * .14 - bleed;
			const top = bandHeight * index - (index === 0 ? bleed : 0);
			const bottom = bandHeight * (index + 1) + (index === tones.length - 1 ? bleed : 1);
			ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(left, top, width + bleed * 2 - left - bleed, bottom - top), tone, .94, ctx.gradient), {
				delay: index * .06,
				span: .55,
				enterDX: -width * .25
			});
		});
	};
	const TEMPERA_BAND_COMPOSITIONS = {
		"band-strip": bandStrip,
		"horizon-band": horizonBand,
		"deep-dive": deepDive,
		"tone-ramp": toneRamp,
		"double-band": doubleBand,
		"tilt-band": tiltBand,
		"edge-rails": edgeRails,
		"gradient-wall": gradientWall,
		"terrace": terrace
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/tempera/compositions/temperaFrameCompositions.ts
	const frameWindow = (ctx) => {
		const { width, height, palette } = ctx;
		const cx = width / 2;
		const cy = height / 2;
		const rx = width * .42;
		const ry = height * .44;
		ctx.add(drawHatchFill(ctx.pixi, diamondPolygon(cx, cy, rx * .78, ry * .78), buildHatchSpec(ctx.seed, 23, 1.4), palette.tone2, .45), {
			grow: true,
			span: .6
		});
		ctx.add(drawConcentricDiamonds(ctx.pixi, cx, cy, rx, ry, 3, palette.ink, .9), {
			delay: .08,
			enterDY: height * .06,
			span: .55
		});
		if (!ctx.showDecor) return;
		ctx.add(drawCrossMarks(ctx.pixi, buildCrossRow(ctx.seed, 29, width * .08, height * .14, 3, width * .045, 8), palette.tone4, 1.8, .85), { delay: .22 });
		ctx.add(drawSquareMarks(ctx.pixi, buildDotRow(ctx.seed, 37, width * .92, height * .62, 4, height * .06, 7), palette.tone4, .8), { delay: .28 });
	};
	const doubleFrame = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const offset = width * .05;
		const box = rectPolygon(width * .16, height * .2, width * .68, height * .6);
		const shifted = rectPolygon(width * .16 + offset, height * .2 + offset * .6, width * .68, height * .6);
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, height + bleed * 2), palette.tone1, .9, ctx.gradient), { span: .5 });
		ctx.add(drawPolygonFill(ctx.pixi, shifted, palette.tone3, .8, ctx.gradient), {
			delay: .06,
			span: .55,
			enterDX: offset * 3
		});
		ctx.add(drawHatchFill(ctx.pixi, shifted, buildHatchSpec(ctx.seed, 43), palette.tone4, .4), {
			delay: .1,
			span: .55,
			grow: true
		});
		ctx.add(drawPolygonOutline(ctx.pixi, box, palette.ink, 3.5, .9), {
			delay: .14,
			span: .5,
			enterDY: -height * .08
		});
	};
	const circleWindow = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const radius = Math.min(width, height) * .34;
		const circle = circlePolygon(width / 2, height / 2, radius);
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, height + bleed * 2), palette.tone3, .92, ctx.gradient), { span: .5 });
		ctx.add(drawHatchFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, height + bleed * 2), buildHatchSpec(ctx.seed, 47), palette.tone4, .4), {
			delay: .04,
			span: .6,
			grow: true
		});
		ctx.add(drawPolygonFill(ctx.pixi, circle, palette.paper, .95, ctx.gradient), {
			delay: .08,
			span: .55
		});
		ctx.add(drawPolygonOutline(ctx.pixi, circle, palette.ink, 3, .9), {
			delay: .14,
			span: .5
		});
		if (!ctx.showDecor) return;
		ctx.add(drawPolygonOutline(ctx.pixi, circlePolygon(width / 2, height / 2, radius * 1.12), palette.ink, 1.2, .55), {
			delay: .2,
			drift: true
		});
	};
	const ladderFrame = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const left = width * .14;
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, height + bleed * 2), palette.tone1, .9, ctx.gradient), { span: .5 });
		const steps = 5;
		for (let index = 0; index < steps; index += 1) {
			const y = height * (.18 + index * .14);
			const run = width * (.06 + index * .03);
			ctx.add(drawLines(ctx.pixi, [{
				x1: left,
				y1: y,
				x2: left + run,
				y2: y
			}, {
				x1: left,
				y1: y,
				x2: left,
				y2: y + height * .14
			}], palette.tone4, index % 2 === 0 ? 3 : 1.4, .85), {
				delay: index * .05,
				span: .5,
				enterDX: -width * .1
			});
		}
		ctx.add(drawLines(ctx.pixi, [{
			x1: width * .9,
			y1: -bleed,
			x2: width * .9,
			y2: height + bleed
		}], palette.tone4, 1.4, .6), {
			delay: .28,
			span: .5,
			enterDY: height * .2
		});
	};
	const cornerBrackets = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const inset = Math.min(width, height) * .12;
		const arm = Math.min(width, height) * .16;
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, height + bleed * 2), palette.tone2, .9, ctx.gradient), { span: .5 });
		[
			[
				inset,
				inset,
				1,
				1
			],
			[
				width - inset,
				inset,
				-1,
				1
			],
			[
				inset,
				height - inset,
				1,
				-1
			],
			[
				width - inset,
				height - inset,
				-1,
				-1
			]
		].forEach(([x, y, sx, sy], index) => {
			ctx.add(drawLines(ctx.pixi, [{
				x1: x,
				y1: y,
				x2: x + sx * arm,
				y2: y
			}, {
				x1: x,
				y1: y,
				x2: x,
				y2: y + sy * arm
			}], palette.ink, 3.5, .9), {
				delay: index * .05,
				span: .5,
				enterDX: sx * width * .06,
				enterDY: sy * height * .06
			});
		});
		if (!ctx.showDecor) return;
		const dotted = visualizerHash01(ctx.seed, 2, 53) > .5;
		ctx.add(dotted ? drawSquareMarks(ctx.pixi, buildDotRow(ctx.seed, 59, width * .5, height * .16, 3, width * .03, 6, 0), palette.tone4, .8) : drawCrossMarks(ctx.pixi, buildCrossRow(ctx.seed, 61, width * .44, height * .84, 3, width * .04, 7), palette.tone4, 1.8, .8), {
			delay: .24,
			drift: true
		});
	};
	const insetBox = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const box = rectPolygon(width * .14, height * .2, width * .72, height * .6);
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, height + bleed * 2), palette.tone1, .9, ctx.gradient), { span: .5 });
		ctx.add(drawPolygonFill(ctx.pixi, box, palette.tone3, .55, ctx.gradient), {
			delay: .05,
			span: .55,
			enterDY: height * .06
		});
		ctx.add(drawPolygonOutline(ctx.pixi, box, palette.ink, 2.4, .85), {
			delay: .1,
			span: .5
		});
	};
	const bracketPair = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const inset = width * .16;
		const arm = width * .09;
		const top = height * .26;
		const bottom = height * .74;
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, height + bleed * 2), palette.tone2, .9, ctx.gradient), { span: .5 });
		[1, -1].forEach((side, index) => {
			const x = side === 1 ? inset : width - inset;
			ctx.add(drawLines(ctx.pixi, [
				{
					x1: x,
					y1: top,
					x2: x + side * arm,
					y2: top
				},
				{
					x1: x,
					y1: top,
					x2: x,
					y2: bottom
				},
				{
					x1: x,
					y1: bottom,
					x2: x + side * arm,
					y2: bottom
				}
			], palette.ink, 4, .9), {
				delay: index * .07,
				span: .5,
				enterDX: side * width * .08
			});
		});
	};
	const archWindow = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const radius = width * .24;
		const cx = width / 2;
		const shoulder = height * .42;
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, height + bleed * 2), palette.tone3, .92, ctx.gradient), { span: .5 });
		const arch = circlePolygon(cx, shoulder, radius, 48);
		const body = rectPolygon(cx - radius, shoulder, radius * 2, height * .4);
		ctx.add(drawPolygonFill(ctx.pixi, arch, palette.paper, .95, ctx.gradient), {
			delay: .05,
			span: .55,
			enterDY: -height * .08
		});
		ctx.add(drawPolygonFill(ctx.pixi, body, palette.paper, .95, ctx.gradient), {
			delay: .05,
			span: .55,
			enterDY: height * .08
		});
		ctx.add(drawPolygonOutline(ctx.pixi, arch, palette.ink, 2.4, .8), {
			delay: .12,
			span: .5
		});
		ctx.add(drawPolygonOutline(ctx.pixi, body, palette.ink, 2.4, .8), {
			delay: .12,
			span: .5
		});
	};
	const gridCells = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, height + bleed * 2), palette.tone1, .9, ctx.gradient), { span: .5 });
		const left = width * .12;
		const top = height * .18;
		const cellWidth = width * .76 / 3;
		const cellHeight = height * .64 / 3;
		for (let row = 0; row < 3; row += 1) for (let column = 0; column < 3; column += 1) {
			const cell = rectPolygon(left + cellWidth * column, top + cellHeight * row, cellWidth, cellHeight);
			const index = row * 3 + column;
			if (row === 1 && column === 1) ctx.add(drawPolygonFill(ctx.pixi, cell, palette.tone4, .75, ctx.gradient), {
				delay: index * .03,
				span: .5
			});
			ctx.add(drawPolygonOutline(ctx.pixi, cell, palette.tone4, 1.2, .6), {
				delay: index * .03,
				span: .5
			});
		}
	};
	const keyhole = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const radius = Math.min(width, height) * .19;
		const cx = width / 2;
		const cy = height * .36;
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, height + bleed * 2), palette.tone4, .94, ctx.gradient), { span: .5 });
		const head = circlePolygon(cx, cy, radius, 48);
		const shaft = rectPolygon(cx - radius * .55, cy, radius * 1.1, height * .46);
		ctx.add(drawPolygonFill(ctx.pixi, head, palette.paper, .96, ctx.gradient), {
			delay: .05,
			span: .55
		});
		ctx.add(drawPolygonFill(ctx.pixi, shaft, palette.paper, .96, ctx.gradient), {
			delay: .08,
			span: .55,
			enterDY: height * .1
		});
		ctx.add(drawPolygonOutline(ctx.pixi, head, palette.ink, 2, .7), {
			delay: .14,
			span: .5
		});
	};
	const TEMPERA_FRAME_COMPOSITIONS = {
		"frame-window": frameWindow,
		"double-frame": doubleFrame,
		"circle-window": circleWindow,
		"ladder-frame": ladderFrame,
		"corner-brackets": cornerBrackets,
		"inset-box": insetBox,
		"bracket-pair": bracketPair,
		"arch-window": archWindow,
		"grid-cells": gridCells,
		"keyhole": keyhole
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/tempera/compositions/temperaPosterCompositions.ts
	const posterPanel = (ctx) => {
		const { width, height, palette } = ctx;
		const poster = ctx.createGroup(-.06, width / 2, height / 2);
		const solid = diamondPolygon(-width * .12, 0, width * .42, height * .66);
		const hatched = diamondPolygon(width * .2, -height * .06, width * .3, height * .48);
		ctx.add(drawPolygonFill(ctx.pixi, solid, palette.ink, .92, ctx.gradient), {
			enterDX: -width * .6,
			span: .55
		}, poster);
		ctx.add(drawHatchFill(ctx.pixi, hatched, buildHatchSpec(ctx.seed, 41), palette.tone4, .7), {
			delay: .08,
			grow: true,
			span: .55
		}, poster);
		ctx.add(drawPolygonOutline(ctx.pixi, hatched, palette.ink, 2, .8), {
			delay: .12,
			span: .55
		}, poster);
		ctx.add(drawPolyline(ctx.pixi, buildWavyPath(ctx.seed, 43, -width * .6, width * .6, height * .42, height * .02), palette.tone4, 2, .7), {
			delay: .2,
			enterDY: height * .1
		}, poster);
	};
	const diamondStack = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, height + bleed * 2), palette.tone1, .9, ctx.gradient), { span: .5 });
		[
			palette.ink,
			palette.tone4,
			palette.tone3
		].forEach((tone, index) => {
			const scale = 1 - index * .28;
			const cx = width * (.34 + index * .24);
			const cy = height * (.52 - index * .14);
			ctx.add(drawPolygonFill(ctx.pixi, diamondPolygon(cx, cy, width * .3 * scale, height * .46 * scale), tone, .93, ctx.gradient), {
				delay: index * .07,
				span: .55,
				enterDX: width * .25,
				enterDY: -height * .12
			});
		});
	};
	const slashPoster = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const lean = height * .34;
		const band = [
			-bleed,
			height * .24 + lean,
			width + bleed,
			height * .24 - lean,
			width + bleed,
			height * .72 - lean,
			-bleed,
			height * .72 + lean
		];
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, height + bleed * 2), palette.tone1, .9, ctx.gradient), { span: .5 });
		ctx.add(drawPolygonFill(ctx.pixi, band, palette.tone4, .94, ctx.gradient), {
			delay: .05,
			span: .55,
			enterDX: -width * .4
		});
		ctx.add(drawHatchFill(ctx.pixi, band, {
			...buildHatchSpec(ctx.seed, 67),
			angle: Math.PI / 3
		}, palette.paper, .3), {
			delay: .1,
			span: .55,
			grow: true
		});
		ctx.add(drawPolygonOutline(ctx.pixi, band, palette.ink, 2.2, .8), {
			delay: .14,
			span: .5
		});
	};
	const arrowWedge = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const apex = height * (.24 + visualizerHash01(ctx.seed, 3, 71) * .14);
		const thickness = height * .2;
		const chevron = [
			-bleed,
			height + bleed,
			width * .5,
			apex,
			width + bleed,
			height + bleed,
			width + bleed,
			height + bleed,
			width * .5,
			apex + thickness,
			-bleed,
			height + bleed
		];
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, height + bleed * 2), palette.tone1, .9, ctx.gradient), { span: .5 });
		ctx.add(drawPolygonFill(ctx.pixi, [
			-bleed,
			height + bleed,
			width * .5,
			apex,
			width + bleed,
			height + bleed
		], palette.tone4, .94, ctx.gradient), {
			delay: .05,
			span: .55,
			enterDY: height * .3
		});
		ctx.add(drawHatchFill(ctx.pixi, chevron, {
			...buildHatchSpec(ctx.seed, 73),
			angle: -Math.PI / 4
		}, palette.paper, .35), {
			delay: .1,
			span: .55,
			grow: true
		});
		ctx.add(drawPolyline(ctx.pixi, [
			-bleed,
			height * .9,
			width * .5,
			apex - thickness * .4,
			width + bleed,
			height * .9
		], palette.ink, 2, .7), {
			delay: .16,
			span: .5,
			enterDY: -height * .1
		});
	};
	const edgeBleed = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const fromLeft = visualizerHash01(ctx.seed, 4, 79) > .5;
		const cover = width * .42;
		const mass = fromLeft ? rectPolygon(-bleed, -bleed, cover + bleed, height + bleed * 2) : rectPolygon(width - cover, -bleed, cover + bleed, height + bleed * 2);
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, height + bleed * 2), palette.tone1, .9, ctx.gradient), { span: .5 });
		ctx.add(drawPolygonFill(ctx.pixi, mass, palette.ink, .93, ctx.gradient), {
			delay: .05,
			span: .55,
			enterDX: (fromLeft ? -1 : 1) * width * .4
		});
		ctx.add(drawHatchFill(ctx.pixi, mass, buildHatchSpec(ctx.seed, 83), palette.paper, .22), {
			delay: .12,
			span: .55,
			grow: true
		});
	};
	const triangleMass = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const fromLeft = visualizerHash01(ctx.seed, 91, 89) > .5;
		const apexX = fromLeft ? width * 1.02 : -width * .02;
		const triangle = [
			fromLeft ? -bleed : width + bleed,
			-bleed,
			fromLeft ? -bleed : width + bleed,
			height + bleed,
			apexX,
			height * (.3 + visualizerHash01(ctx.seed, 93, 97) * .3)
		];
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, height + bleed * 2), palette.tone1, .9, ctx.gradient), { span: .5 });
		ctx.add(drawPolygonFill(ctx.pixi, triangle, palette.tone4, .94, ctx.gradient), {
			delay: .05,
			span: .55,
			enterDX: (fromLeft ? -1 : 1) * width * .35
		});
		ctx.add(drawHatchFill(ctx.pixi, triangle, buildHatchSpec(ctx.seed, 101), palette.paper, .24), {
			delay: .12,
			span: .55,
			grow: true
		});
	};
	const ribbonCross = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const half = height * .14;
		const lean = height * .3;
		const ribbon = (sign) => [
			-bleed,
			height / 2 + sign * lean - half,
			width + bleed,
			height / 2 - sign * lean - half,
			width + bleed,
			height / 2 - sign * lean + half,
			-bleed,
			height / 2 + sign * lean + half
		];
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, height + bleed * 2), palette.tone1, .9, ctx.gradient), { span: .5 });
		ctx.add(drawPolygonFill(ctx.pixi, ribbon(1), palette.tone3, .8, ctx.gradient), {
			delay: .05,
			span: .55,
			enterDX: -width * .3
		});
		ctx.add(drawPolygonFill(ctx.pixi, ribbon(-1), palette.tone4, .8, ctx.gradient), {
			delay: .11,
			span: .55,
			enterDX: width * .3
		});
	};
	const halfDisc = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const fromRight = visualizerHash01(ctx.seed, 103, 107) > .5;
		const radius = Math.hypot(width, height) * .62;
		const disc = circlePolygon(fromRight ? width + radius * .55 : -radius * .55, height * .5, radius, 64);
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, height + bleed * 2), palette.tone1, .9, ctx.gradient), { span: .5 });
		ctx.add(drawPolygonFill(ctx.pixi, disc, palette.tone4, .94, ctx.gradient), {
			delay: .05,
			span: .55,
			enterDX: (fromRight ? 1 : -1) * width * .3
		});
		ctx.add(drawPolygonOutline(ctx.pixi, disc, palette.ink, 2, .6), {
			delay: .12,
			span: .5
		});
	};
	const stackedSlabs = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, height + bleed * 2), palette.tone1, .9, ctx.gradient), { span: .5 });
		[
			palette.tone2,
			palette.tone3,
			palette.tone4
		].forEach((tone, index) => {
			const group = ctx.createGroup(-.09 + index * .08, width / 2, height / 2);
			const slab = rectPolygon(-width * (.42 - index * .05), -height * (.3 - index * .03), width * (.84 - index * .1), height * (.6 - index * .06));
			ctx.add(drawPolygonFill(ctx.pixi, slab, tone, .93, ctx.gradient), {
				delay: index * .07,
				span: .55,
				enterDX: width * .2,
				enterDY: height * .12
			}, group);
		});
	};
	const wedgePair = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const waist = width * (.2 + visualizerHash01(ctx.seed, 109, 113) * .1);
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, height + bleed * 2), palette.tone1, .9, ctx.gradient), { span: .5 });
		ctx.add(drawPolygonFill(ctx.pixi, [
			-bleed,
			-bleed,
			(width - waist) / 2,
			height / 2,
			-bleed,
			height + bleed
		], palette.tone4, .94, ctx.gradient), {
			delay: .05,
			span: .55,
			enterDX: -width * .25
		});
		ctx.add(drawPolygonFill(ctx.pixi, [
			width + bleed,
			-bleed,
			(width + waist) / 2,
			height / 2,
			width + bleed,
			height + bleed
		], palette.tone4, .94, ctx.gradient), {
			delay: .11,
			span: .55,
			enterDX: width * .25
		});
	};
	const TEMPERA_POSTER_COMPOSITIONS = {
		"poster-panel": posterPanel,
		"diamond-stack": diamondStack,
		"slash-poster": slashPoster,
		"arrow-wedge": arrowWedge,
		"edge-bleed": edgeBleed,
		"triangle-mass": triangleMass,
		"ribbon-cross": ribbonCross,
		"half-disc": halfDisc,
		"stacked-slabs": stackedSlabs,
		"wedge-pair": wedgePair
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/tempera/compositions/temperaSparseCompositions.ts
	const quietLine = (ctx) => {
		const { width, height, palette } = ctx;
		const gridWidth = width * .7;
		const gridX = (width - gridWidth) / 2;
		[
			.38,
			.5,
			.62
		].forEach((ratio, index) => {
			ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(gridX, height * ratio, gridWidth, 1), palette.line, 1, ctx.gradient), {
				delay: index * .08,
				span: .6,
				enterDX: (index % 2 === 0 ? -1 : 1) * width * .2
			});
		});
		if (!ctx.showDecor) return;
		ctx.add(drawPolyline(ctx.pixi, buildScribblePath(ctx.decor.scribbleSeed, 47, width * .2, height * .26, Math.min(width, height) * .09, 2), palette.tone4, 1.6, .7), {
			delay: .24,
			span: .6
		});
		const tuftX = width * .82;
		const tuftY = height * .74;
		ctx.add(drawLines(ctx.pixi, Array.from({ length: 6 }, (_, index) => {
			const lean = (visualizerHash01(ctx.decor.scribbleSeed, index, 59) - .5) * 34;
			return {
				x1: tuftX + index * 7,
				y1: tuftY,
				x2: tuftX + index * 7 + lean,
				y2: tuftY - 24 - index * 3
			};
		}), palette.tone4, 1.4, .7), {
			delay: .3,
			span: .55
		});
	};
	const starfieldDots = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const spacing = Math.max(24, Math.sqrt(width * height / 900));
		const marks = buildDotGrid(width + bleed, height + bleed, spacing, 2.6);
		const dense = marks.filter((mark) => mark.y > height * .45);
		const sparse = marks.filter((mark) => mark.y <= height * .45 && (mark.x + mark.y) % 3 < 1);
		ctx.add(drawSquareMarks(ctx.pixi, dense, palette.tone4, .45), {
			span: .6,
			enterDY: height * .2
		});
		ctx.add(drawSquareMarks(ctx.pixi, sparse, palette.tone4, .25), {
			delay: .08,
			span: .6,
			enterDY: -height * .15
		});
		if (!ctx.showDecor) return;
		ctx.add(drawLines(ctx.pixi, [{
			x1: -bleed,
			y1: height * .45,
			x2: width + bleed,
			y2: height * .45
		}], palette.tone4, 1.2, .5), {
			delay: .2,
			span: .5,
			enterDX: width * .2
		});
	};
	const rippleLines = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const count = 7;
		for (let index = 0; index < count; index += 1) {
			const y = height * (.16 + index * .11);
			const amplitude = height * (.006 + index * .004);
			ctx.add(drawPolyline(ctx.pixi, buildWavyPath(ctx.seed, 89 + index, -bleed, width + bleed, y, amplitude, 26), palette.tone4, index % 3 === 0 ? 2 : 1.1, .55), {
				delay: index * .045,
				span: .6,
				enterDX: (index % 2 === 0 ? -1 : 1) * width * .15
			});
		}
	};
	const hairGrid = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const columns = 6;
		const rows = 4;
		const vertical = Array.from({ length: 7 }, (_, index) => ({
			x1: width / columns * index,
			y1: -bleed,
			x2: width / columns * index,
			y2: height + bleed
		}));
		const horizontal = Array.from({ length: 5 }, (_, index) => ({
			x1: -bleed,
			y1: height / rows * index,
			x2: width + bleed,
			y2: height / rows * index
		}));
		ctx.add(drawLines(ctx.pixi, vertical, palette.line, 1, .8), {
			span: .6,
			enterDY: -height * .1
		});
		ctx.add(drawLines(ctx.pixi, horizontal, palette.line, 1, .8), {
			delay: .08,
			span: .6,
			enterDX: width * .1
		});
	};
	const marginRule = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const fromLeft = visualizerHash01(ctx.seed, 121, 127) > .5;
		const x = fromLeft ? width * .12 : width * .88;
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(x - 3, -bleed, 6, height + bleed * 2), palette.tone4, .9, ctx.gradient), {
			span: .6,
			enterDY: height * .15
		});
		if (!ctx.showDecor) return;
		ctx.add(drawSquareMarks(ctx.pixi, [
			0,
			1,
			2
		].map((index) => ({
			x: x + (fromLeft ? 22 : -22),
			y: height * (.32 + index * .18),
			size: 7,
			rotation: 0
		})), palette.tone4, .8), {
			delay: .22,
			drift: true
		});
	};
	const dotDrift = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const spacing = Math.max(22, Math.sqrt(width * height / 1200));
		const marks = buildDotGrid(width + bleed, height + bleed, spacing, 2.4);
		const span = width + height;
		const near = marks.filter((mark) => mark.x + mark.y > span * .5);
		const far = marks.filter((mark) => mark.x + mark.y <= span * .5 && (mark.x + mark.y) % 2 < 1);
		ctx.add(drawSquareMarks(ctx.pixi, near, palette.tone4, .5), {
			span: .6,
			enterDX: width * .12
		});
		ctx.add(drawSquareMarks(ctx.pixi, far, palette.tone4, .24), {
			delay: .08,
			span: .6,
			enterDX: -width * .12
		});
	};
	const arcSweep = (ctx) => {
		const { width, height, palette } = ctx;
		const cx = width * (visualizerHash01(ctx.seed, 131, 137) > .5 ? 1.15 : -.15);
		const cy = height * 1.05;
		const base = Math.hypot(width, height) * .42;
		for (let index = 0; index < 5; index += 1) ctx.add(drawPolygonOutline(ctx.pixi, circlePolygon(cx, cy, base + index * base * .22, 72), palette.tone4, index % 2 === 0 ? 1.8 : 1, .55), {
			delay: index * .05,
			span: .6,
			enterDY: height * .08
		});
	};
	const blankPage = (ctx) => {
		const { width, height, palette } = ctx;
		ctx.add(drawLines(ctx.pixi, [{
			x1: width * .18,
			y1: height * .7,
			x2: width * .36,
			y2: height * .7
		}], palette.line, 1.4, .8), {
			span: .6,
			enterDX: -width * .12
		});
		if (!ctx.showDecor) return;
		ctx.add(drawPolyline(ctx.pixi, buildScribblePath(ctx.decor.scribbleSeed, 139, width * .82, height * .26, Math.min(width, height) * .06, 2), palette.tone4, 1.4, .6), {
			delay: .28,
			span: .6
		});
	};
	const TEMPERA_SPARSE_COMPOSITIONS = {
		"quiet-line": quietLine,
		"starfield-dots": starfieldDots,
		"ripple-lines": rippleLines,
		"hair-grid": hairGrid,
		"margin-rule": marginRule,
		"dot-drift": dotDrift,
		"arc-sweep": arcSweep,
		"blank-page": blankPage
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/tempera/compositions/temperaCinemaCompositions.ts
/**
	* Fits a window of the given aspect inside the frame. Real pixel dimensions are used rather
	* than viewport fractions, because a "square" window has to look square on any display.
	*/
	const fitWindow = (width, height, aspect, fill) => {
		const maxWidth = width * fill;
		const maxHeight = height * fill;
		const windowWidth = Math.min(maxWidth, maxHeight * aspect);
		const windowHeight = windowWidth / aspect;
		return {
			x: (width - windowWidth) / 2,
			y: (height - windowHeight) / 2,
			width: windowWidth,
			height: windowHeight
		};
	};
	/**
	* Draws the matte as four bars rather than a filled shape with a hole: the bars stay convex,
	* which the hatch clipper needs, and they can each carry their own entrance.
	*/
	const addMatte = (ctx, hole, tone) => {
		const { width, height, bleed } = ctx;
		[
			{
				polygon: rectPolygon(-bleed, -bleed, width + bleed * 2, hole.y + bleed),
				enterDX: 0,
				enterDY: -height * .2
			},
			{
				polygon: rectPolygon(-bleed, hole.y + hole.height, width + bleed * 2, height - hole.y - hole.height + bleed),
				enterDX: 0,
				enterDY: height * .2
			},
			{
				polygon: rectPolygon(-bleed, hole.y, hole.x + bleed, hole.height),
				enterDX: -width * .2,
				enterDY: 0
			},
			{
				polygon: rectPolygon(hole.x + hole.width, hole.y, width - hole.x - hole.width + bleed, hole.height),
				enterDX: width * .2,
				enterDY: 0
			}
		].forEach((bar, index) => {
			ctx.add(drawPolygonFill(ctx.pixi, bar.polygon, tone, .96, ctx.gradient), {
				delay: index * .04,
				span: .5,
				enterDX: bar.enterDX,
				enterDY: bar.enterDY
			});
		});
		ctx.add(drawPolygonOutline(ctx.pixi, rectPolygon(hole.x, hole.y, hole.width, hole.height), ctx.palette.ink, 1.6, .5), {
			delay: .2,
			span: .5
		});
	};
	const addWindowWash = (ctx, hole, alpha) => {
		const polygon = rectPolygon(hole.x, hole.y, hole.width, hole.height);
		ctx.add(drawPolygonFill(ctx.pixi, polygon, ctx.palette.tone1, alpha, ctx.gradient), { span: .55 });
		ctx.add(drawHatchFill(ctx.pixi, polygon, buildHatchSpec(ctx.seed, 149, 1.5), ctx.palette.tone4, .2), {
			delay: .1,
			span: .6,
			grow: true
		});
	};
	const matte = (aspect, fill, washAlpha = .35) => (ctx) => {
		const hole = fitWindow(ctx.width, ctx.height, aspect, fill);
		addWindowWash(ctx, hole, washAlpha);
		addMatte(ctx, hole, ctx.palette.tone4);
		if (!ctx.showDecor) return;
		ctx.add(drawLines(ctx.pixi, [{
			x1: hole.x,
			y1: hole.y - ctx.height * .05,
			x2: hole.x,
			y2: hole.y - ctx.height * .02
		}, {
			x1: hole.x + hole.width,
			y1: hole.y + hole.height + ctx.height * .02,
			x2: hole.x + hole.width,
			y2: hole.y + hole.height + ctx.height * .05
		}], ctx.palette.paper, 2, .6), {
			delay: .26,
			span: .5
		});
	};
	const cinemaTwin = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const inset = height * .16;
		const gutter = width * .04;
		const left = {
			x: width * .08,
			y: inset,
			width: width * .5,
			height: height - inset * 2
		};
		const right = {
			x: left.x + left.width + gutter,
			y: inset + height * .1,
			width: width * .28,
			height: height - inset * 2 - height * .2
		};
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, height + bleed * 2), palette.tone4, .96, ctx.gradient), { span: .5 });
		[left, right].forEach((hole, index) => {
			const polygon = rectPolygon(hole.x, hole.y, hole.width, hole.height);
			ctx.add(drawPolygonFill(ctx.pixi, polygon, palette.tone1, index === 0 ? .4 : .85, ctx.gradient), {
				delay: index * .08,
				span: .55,
				enterDX: (index === 0 ? -1 : 1) * width * .14
			});
			ctx.add(drawPolygonOutline(ctx.pixi, polygon, palette.ink, 1.6, .5), {
				delay: .16 + index * .05,
				span: .5
			});
		});
		if (!ctx.showDecor) return;
		ctx.add(drawHatchFill(ctx.pixi, rectPolygon(right.x, right.y, right.width, right.height), buildHatchSpec(ctx.seed, 151), palette.tone4, .35), {
			delay: .24,
			span: .6,
			grow: true
		});
	};
	const jitteredFill = (ctx, base) => base + (visualizerHash01(ctx.seed, 157, 163) - .5) * .06;
	const TEMPERA_CINEMA_COMPOSITIONS = {
		"cinema-scope": (ctx) => matte(2.39, jitteredFill(ctx, .88))(ctx),
		"cinema-wide": (ctx) => matte(1.85, jitteredFill(ctx, .84))(ctx),
		"cinema-academy": (ctx) => matte(1.33, jitteredFill(ctx, .78))(ctx),
		"cinema-square": (ctx) => matte(1, jitteredFill(ctx, .72))(ctx),
		"cinema-portrait": (ctx) => matte(.75, jitteredFill(ctx, .72))(ctx),
		"cinema-tall": (ctx) => matte(.5625, jitteredFill(ctx, .72))(ctx),
		"cinema-twin": cinemaTwin
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/tempera/temperaCurves.ts
	const TAU = Math.PI * 2;
	/** Convex. */
	const ellipsePolygon = (cx, cy, rx, ry, segments = 32) => {
		const count = Math.max(8, Math.round(segments));
		const points = [];
		for (let index = 0; index < count; index += 1) {
			const angle = index / count * TAU;
			points.push(cx + Math.cos(angle) * rx, cy + Math.sin(angle) * ry);
		}
		return points;
	};
	/** Convex. The sticker plate: a rectangle whose corners are quarter arcs. */
	const roundedRectPolygon = (x, y, width, height, radius, cornerSegments = 6) => {
		const limit = Math.max(0, Math.min(radius, Math.min(width, height) / 2));
		const steps = Math.max(1, Math.round(cornerSegments));
		const corners = [
			[
				x + width - limit,
				y + limit,
				-Math.PI / 2
			],
			[
				x + width - limit,
				y + height - limit,
				0
			],
			[
				x + limit,
				y + height - limit,
				Math.PI / 2
			],
			[
				x + limit,
				y + limit,
				Math.PI
			]
		];
		const points = [];
		corners.forEach(([ccx, ccy, start]) => {
			for (let step = 0; step <= steps; step += 1) {
				const angle = start + step / steps * (Math.PI / 2);
				points.push(ccx + Math.cos(angle) * limit, ccy + Math.sin(angle) * limit);
			}
		});
		return points;
	};
	/** Rotates a polygon about a pivot. Convexity is preserved, so hatchable shapes stay hatchable. */
	const rotatePolygon = (polygon, cx, cy, angle) => {
		const cos = Math.cos(angle);
		const sin = Math.sin(angle);
		const points = [];
		for (let index = 0; index < polygon.length; index += 2) {
			const dx = polygon[index] - cx;
			const dy = polygon[index + 1] - cy;
			points.push(cx + dx * cos - dy * sin, cy + dx * sin + dy * cos);
		}
		return points;
	};
	/**
	* Concave. An outline with `lobes` sinusoidal bumps, normalised so it still fits exactly in
	* rx/ry. A small amplitude reads as a cloud or a lace medallion, a large one as a rosette.
	*/
	const lobedPolygon = (cx, cy, rx, ry, lobes, amplitude, segments = 96) => {
		const count = Math.max(24, Math.round(segments));
		const petals = Math.max(1, Math.round(lobes));
		const swell = Math.max(0, amplitude);
		const points = [];
		for (let index = 0; index < count; index += 1) {
			const angle = index / count * TAU;
			const scale = (1 + Math.cos(angle * petals) * swell) / (1 + swell);
			points.push(cx + Math.cos(angle) * rx * scale, cy + Math.sin(angle) * ry * scale);
		}
		return points;
	};
	/**
	* Concave. A band with a flat edge at `flatY` and a row of tangent half-round bumps hanging
	* off `baseY` - the frill. Bump radius is half the bump width, so the count alone sets how
	* fine the lace reads.
	*/
	const scallopBandPolygon = (x0, x1, flatY, baseY, bumps, segmentsPerBump = 10) => {
		const count = Math.max(1, Math.round(bumps));
		const steps = Math.max(3, Math.round(segmentsPerBump));
		const span = (x1 - x0) / count;
		const radius = Math.abs(span) / 2;
		const direction = baseY >= flatY ? 1 : -1;
		const points = [
			x0,
			flatY,
			x1,
			flatY
		];
		for (let bump = count - 1; bump >= 0; bump -= 1) {
			const cx = x0 + span * (bump + .5);
			for (let step = 0; step <= steps; step += 1) {
				const angle = step / steps * Math.PI;
				points.push(cx + Math.cos(angle) * (span / 2), baseY + direction * Math.sin(angle) * radius);
			}
		}
		return points;
	};
	/**
	* Concave. The classic parametric heart, sampled and then normalised into the rx/ry box, so
	* callers size it like any other shape instead of carrying the curve's own scale.
	*/
	const heartPolygon = (cx, cy, rx, ry, segments = 64) => {
		const count = Math.max(16, Math.round(segments));
		const raw = [];
		let minX = Number.POSITIVE_INFINITY;
		let maxX = Number.NEGATIVE_INFINITY;
		let minY = Number.POSITIVE_INFINITY;
		let maxY = Number.NEGATIVE_INFINITY;
		for (let index = 0; index < count; index += 1) {
			const t = index / count * TAU;
			const x = Math.sin(t) ** 3 * 16;
			const y = -(13 * Math.cos(t) - 5 * Math.cos(2 * t) - 2 * Math.cos(3 * t) - Math.cos(4 * t));
			raw.push(x, y);
			minX = Math.min(minX, x);
			maxX = Math.max(maxX, x);
			minY = Math.min(minY, y);
			maxY = Math.max(maxY, y);
		}
		const halfWidth = Math.max(1e-6, (maxX - minX) / 2);
		const halfHeight = Math.max(1e-6, (maxY - minY) / 2);
		const midX = (minX + maxX) / 2;
		const midY = (minY + maxY) / 2;
		const points = [];
		for (let index = 0; index < raw.length; index += 2) points.push(cx + (raw[index] - midX) / halfWidth * rx, cy + (raw[index + 1] - midY) / halfHeight * ry);
		return points;
	};
	/**
	* Concave. One segment of a ring, from `startAngle` to `endAngle`. A ring of these leaves the
	* spokes between them and the hub inside them standing - which is how a ring gets punched out
	* of a plate without the middle falling out of it.
	*/
	const annularSectorPolygon = (cx, cy, innerRadius, outerRadius, startAngle, endAngle, segments = 10) => {
		const steps = Math.max(2, Math.round(segments));
		const points = [];
		for (let step = 0; step <= steps; step += 1) {
			const angle = startAngle + (endAngle - startAngle) * step / steps;
			points.push(cx + Math.cos(angle) * outerRadius, cy + Math.sin(angle) * outerRadius);
		}
		for (let step = steps; step >= 0; step -= 1) {
			const angle = startAngle + (endAngle - startAngle) * step / steps;
			points.push(cx + Math.cos(angle) * innerRadius, cy + Math.sin(angle) * innerRadius);
		}
		return points;
	};
	/** Concave. Four tips with a tight waist is the VN sparkle; more tips read as a star. */
	const starPolygon = (cx, cy, outerRadius, innerRadius, points, rotation = 0) => {
		const tips = Math.max(3, Math.round(points));
		const polygon = [];
		for (let index = 0; index < tips * 2; index += 1) {
			const radius = index % 2 === 0 ? outerRadius : innerRadius;
			const angle = rotation - Math.PI / 2 + index / (tips * 2) * TAU;
			polygon.push(cx + Math.cos(angle) * radius, cy + Math.sin(angle) * radius);
		}
		return polygon;
	};
	/**
	* Concave. A band whose centreline sags along half a sine wave and whose ends are cut square,
	* so it can run past both frame edges and still read as one curved ribbon.
	*/
	const arcRibbonPolygon = (x0, x1, y, thickness, sag, steps = 28) => {
		const count = Math.max(4, Math.round(steps));
		const half = thickness / 2;
		const top = [];
		const bottom = [];
		for (let index = 0; index <= count; index += 1) {
			const progress = index / count;
			const x = x0 + (x1 - x0) * progress;
			const centre = y + Math.sin(progress * Math.PI) * sag;
			top.push(x, centre - half);
			bottom.push(x, centre + half);
		}
		const points = [...top];
		for (let index = bottom.length - 2; index >= 0; index -= 2) points.push(bottom[index], bottom[index + 1]);
		return points;
	};
	/**
	* A seeded disc field on a jittered lattice. The lattice keeps the coverage even across the
	* frame; the per-cell jitter is what stops it reading as a grid of dots.
	*/
	const buildDiscField = (seed, salt, width, height, count, radius) => {
		const total = Math.max(0, Math.round(count));
		if (total === 0 || width <= 0 || height <= 0) return [];
		const columns = Math.max(1, Math.round(Math.sqrt(total * (width / height))));
		const rows = Math.max(1, Math.ceil(total / columns));
		const cellWidth = width / columns;
		const cellHeight = height / rows;
		const discs = [];
		for (let index = 0; index < total; index += 1) {
			const column = index % columns;
			const row = Math.floor(index / columns);
			discs.push({
				x: cellWidth * (column + .15 + visualizerHash01(seed, index, salt) * .7),
				y: cellHeight * (row + .15 + visualizerHash01(seed, index, salt + 3) * .7),
				radius: radius * (.55 + visualizerHash01(seed, index, salt + 7) * .75)
			});
		}
		return discs;
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/tempera/compositions/temperaCharmCompositions.ts
/** Every card starts from one flat field; the curved shapes sit on top of it. */
	const addField$2 = (ctx, color, alpha = .92) => {
		const { width, height, bleed } = ctx;
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, height + bleed * 2), color, alpha, ctx.gradient), { span: .5 });
	};
	const bubbleDrift = (ctx) => {
		const { width, height, palette } = ctx;
		const unit = Math.min(width, height);
		addField$2(ctx, palette.tone1);
		const big = buildDiscField(ctx.seed, 11, width, height, 8, unit * .115);
		const small = buildDiscField(ctx.seed, 17, width, height, 13, unit * .042);
		ctx.add(drawDiscs(ctx.pixi, big, palette.tone3, .88, ctx.gradient), {
			delay: .05,
			span: .6,
			enterDY: height * .16,
			drift: true
		});
		ctx.add(drawRings(ctx.pixi, big, palette.ink, 2, .55), {
			delay: .12,
			span: .55,
			enterDY: height * .12
		});
		ctx.add(drawDiscs(ctx.pixi, small, palette.tone4, .7), {
			delay: .18,
			span: .6,
			enterDY: height * .22,
			drift: true
		});
		if (!ctx.showDecor) return;
		ctx.add(drawDiscs(ctx.pixi, big.slice(0, 2).map((disc) => ({
			x: disc.x - disc.radius * .36,
			y: disc.y - disc.radius * .36,
			radius: disc.radius * .16
		})), palette.paper, .85), {
			delay: .28,
			drift: true
		});
	};
	const cloudWindow = (ctx) => {
		const { width, height, palette } = ctx;
		const unit = Math.min(width, height);
		const cx = width / 2;
		const cy = height * .52;
		const rx = width * .3;
		const ry = height * .3;
		addField$2(ctx, palette.tone3);
		const cloud = lobedPolygon(cx, cy, rx, ry, 9, .1);
		ctx.add(drawPolygonFill(ctx.pixi, lobedPolygon(cx, cy + height * .025, rx * 1.05, ry * 1.05, 9, .1), palette.tone4, .5), {
			delay: .04,
			span: .6
		});
		ctx.add(drawPolygonFill(ctx.pixi, cloud, palette.paper, .96, ctx.gradient), {
			delay: .08,
			span: .55
		});
		ctx.add(drawPolygonOutline(ctx.pixi, cloud, palette.ink, 2.4, .85), {
			delay: .12,
			span: .5
		});
		if (!ctx.showDecor) return;
		ctx.add(drawDiscs(ctx.pixi, [{
			x: cx - rx * 1.16,
			y: cy + ry * .66,
			radius: unit * .035
		}, {
			x: cx - rx * 1.32,
			y: cy + ry * .9,
			radius: unit * .02
		}], palette.paper, .92, ctx.gradient), {
			delay: .22,
			drift: true
		});
	};
	const heartBurst = (ctx) => {
		const { width, height, palette } = ctx;
		const lean = visualizerHash01(ctx.seed, 3, 83) > .5 ? 1 : -1;
		const cx = width * .5;
		const cy = height * .52;
		const rx = width * .32;
		const ry = height * .4;
		addField$2(ctx, palette.tone4, .94);
		const main = rotatePolygon(heartPolygon(cx, cy, rx, ry), cx, cy, lean * .06);
		ctx.add(drawPolygonFill(ctx.pixi, rotatePolygon(heartPolygon(cx + lean * width * .022, cy + height * .025, rx, ry), cx, cy, lean * .06), palette.tone2, .6), {
			delay: .04,
			span: .6
		});
		ctx.add(drawPolygonFill(ctx.pixi, main, palette.paper, .95, ctx.gradient), {
			delay: .08,
			span: .55,
			enterDY: height * .1
		});
		ctx.add(drawPolygonOutline(ctx.pixi, main, palette.ink, 3, .9), {
			delay: .14,
			span: .5,
			enterDY: height * .08
		});
		[.4, .24].forEach((scale, index) => {
			const x = lean > 0 ? width * (.88 + index * .08) : width * (.12 - index * .08);
			const y = height * (.24 + index * .42);
			const group = ctx.createGroup(0, x, y);
			ctx.add(drawPolygonFill(ctx.pixi, rotatePolygon(heartPolygon(0, 0, rx * scale, ry * scale), 0, 0, -lean * .22), palette.tone2, .9, ctx.gradient), {
				delay: .2 + index * .06,
				span: .5,
				enterDX: lean * width * .12,
				drift: true
			}, group);
		});
	};
	const sparkleField = (ctx) => {
		const { width, height, palette } = ctx;
		const unit = Math.min(width, height);
		addField$2(ctx, palette.tone1, .88);
		const seeds = buildDiscField(ctx.seed, 23, width, height, 9, unit * .07);
		seeds.slice(0, 5).forEach((disc, index) => {
			const star = starPolygon(0, 0, disc.radius, disc.radius * .16, 4, visualizerHash01(ctx.seed, index, 29) * .9);
			const group = ctx.createGroup(0, disc.x, disc.y);
			ctx.add(drawPolygonFill(ctx.pixi, star, palette.tone4, .9, ctx.gradient), {
				delay: .06 + index * .05,
				span: .5,
				drift: true
			}, group);
		});
		ctx.add(drawDiscs(ctx.pixi, seeds.slice(5).map((disc) => ({
			...disc,
			radius: disc.radius * .22
		})), palette.tone4, .7), {
			delay: .24,
			span: .55,
			drift: true
		});
		ctx.add(drawLines(ctx.pixi, [{
			x1: width * .08,
			y1: height * .74,
			x2: width * .92,
			y2: height * .72
		}], palette.tone4, 1.2, .5), {
			delay: .3,
			span: .5,
			enterDX: -width * .12
		});
	};
	const petalArc = (ctx) => {
		const { width, height, palette } = ctx;
		const fromLeft = visualizerHash01(ctx.seed, 5, 89) > .5;
		const hubX = fromLeft ? -width * .1 : width * 1.1;
		const hubY = height * .12;
		const rx = width * .26;
		const ry = height * .085;
		addField$2(ctx, palette.tone2);
		for (let index = 0; index < 5; index += 1) {
			const base = ellipsePolygon(hubX + (fromLeft ? rx : -rx), hubY, rx, ry, 28);
			const petal = rotatePolygon(base, hubX, hubY, (fromLeft ? 1 : -1) * (.18 + index * .3));
			ctx.add(drawPolygonFill(ctx.pixi, petal, index % 2 === 0 ? palette.tone3 : palette.tone1, .9, ctx.gradient), {
				delay: index * .05,
				span: .55,
				enterDY: -height * .1
			});
			ctx.add(drawPolygonOutline(ctx.pixi, petal, palette.ink, 1.6, .5), {
				delay: .06 + index * .05,
				span: .5
			});
		}
		const hub = ctx.createGroup(0, hubX, hubY);
		ctx.add(drawPolygonFill(ctx.pixi, lobedPolygon(0, 0, width * .11, width * .11, 6, .34), palette.tone4, .9, ctx.gradient), {
			delay: .28,
			span: .5,
			drift: true
		}, hub);
		if (!ctx.showDecor) return;
		ctx.add(drawDiscs(ctx.pixi, [
			0,
			1,
			2
		].map((index) => ({
			x: fromLeft ? width * (.78 + index * .07) : width * (.22 - index * .07),
			y: height * (.7 + index * .1),
			radius: height * (.03 - index * .006)
		})), palette.tone4, .75, ctx.gradient), {
			delay: .32,
			span: .5,
			drift: true
		});
	};
	const scallopBand = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		addField$2(ctx, palette.tone1, .9);
		const top = scallopBandPolygon(-bleed, width + bleed, -bleed, height * .28, 9);
		const bottom = scallopBandPolygon(-bleed, width + bleed, height + bleed, height * .72, 9);
		ctx.add(drawPolygonFill(ctx.pixi, top, palette.tone3, .92, ctx.gradient), {
			delay: .04,
			span: .55,
			enterDY: -height * .18
		});
		ctx.add(drawPolygonFill(ctx.pixi, bottom, palette.tone4, .92, ctx.gradient), {
			delay: .1,
			span: .55,
			enterDY: height * .18
		});
		ctx.add(drawPolygonOutline(ctx.pixi, top, palette.ink, 2, .75), {
			delay: .14,
			span: .5,
			enterDY: -height * .12
		});
		ctx.add(drawPolygonOutline(ctx.pixi, bottom, palette.ink, 2, .75), {
			delay: .18,
			span: .5,
			enterDY: height * .12
		});
		if (!ctx.showDecor) return;
		const pitch = (width + bleed * 2) / 9;
		const beads = Array.from({ length: 5 }, (_, index) => ({
			x: -bleed + pitch * (index + 2),
			y: height * .28,
			radius: Math.min(width, height) * .018
		}));
		ctx.add(drawDiscs(ctx.pixi, beads, palette.ink, .7), {
			delay: .26,
			drift: true
		});
	};
	const ribbonLoop = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const sag = height * (.06 + visualizerHash01(ctx.seed, 7, 97) * .05) * (visualizerHash01(ctx.seed, 9, 97) > .5 ? 1 : -1);
		addField$2(ctx, palette.tone2);
		const band = arcRibbonPolygon(-bleed, width + bleed, height * .5, height * .3, sag);
		const echo = arcRibbonPolygon(-bleed, width + bleed, height * .68, height * .06, -sag);
		ctx.add(drawPolygonFill(ctx.pixi, echo, palette.tone4, .7, ctx.gradient), {
			delay: .04,
			span: .6,
			enterDX: -width * .16
		});
		ctx.add(drawPolygonFill(ctx.pixi, band, palette.paper, .95, ctx.gradient), {
			delay: .08,
			span: .55,
			enterDX: width * .18
		});
		ctx.add(drawPolygonOutline(ctx.pixi, band, palette.ink, 2.6, .85), {
			delay: .14,
			span: .5,
			enterDX: width * .14
		});
		const knotX = width * (visualizerHash01(ctx.seed, 11, 97) > .5 ? .24 : .76);
		const knotY = height * .5 + Math.sin((knotX + bleed) / (width + bleed * 2) * Math.PI) * sag;
		const bow = ctx.createGroup(0, knotX, knotY);
		[-1, 1].forEach((side, index) => {
			const loop = rotatePolygon(ellipsePolygon(side * width * .055, 0, width * .05, height * .07, 26), 0, 0, side * .3);
			ctx.add(drawPolygonFill(ctx.pixi, loop, palette.tone4, .92, ctx.gradient), {
				delay: .2 + index * .04,
				span: .5,
				enterDX: side * width * .06
			}, bow);
			ctx.add(drawPolygonOutline(ctx.pixi, loop, palette.ink, 2, .8), {
				delay: .24 + index * .04,
				span: .5
			}, bow);
		});
		const knot = roundedRectPolygon(-width * .018, -height * .035, width * .036, height * .07, height * .02);
		ctx.add(drawPolygonFill(ctx.pixi, knot, palette.tone4, .95, ctx.gradient), {
			delay: .28,
			span: .5
		}, bow);
		ctx.add(drawPolygonOutline(ctx.pixi, knot, palette.ink, 2, .85), {
			delay: .3,
			span: .5
		}, bow);
	};
	const roundPlate = (ctx) => {
		const { width, height, palette } = ctx;
		const unit = Math.min(width, height);
		addField$2(ctx, palette.tone3);
		const plate = roundedRectPolygon(width * .16, height * .24, width * .68, height * .52, unit * .11);
		const inner = roundedRectPolygon(width * .19, height * .29, width * .62, height * .42, unit * .08);
		ctx.add(drawPolygonFill(ctx.pixi, plate, palette.paper, .95, ctx.gradient), {
			delay: .05,
			span: .55,
			enterDY: height * .06
		});
		ctx.add(drawHatchFill(ctx.pixi, inner, buildHatchSpec(ctx.seed, 101, 1.3), palette.tone2, .35), {
			delay: .1,
			span: .6,
			grow: true
		});
		ctx.add(drawPolygonOutline(ctx.pixi, plate, palette.ink, 3, .9), {
			delay: .12,
			span: .5
		});
		ctx.add(drawPolygonOutline(ctx.pixi, inner, palette.tone4, 1.2, .55), {
			delay: .16,
			span: .5
		});
		if (!ctx.showDecor) return;
		ctx.add(drawDiscs(ctx.pixi, [
			{
				x: width * .22,
				y: height * .3,
				radius: unit * .016
			},
			{
				x: width * .78,
				y: height * .3,
				radius: unit * .016
			},
			{
				x: width * .22,
				y: height * .7,
				radius: unit * .016
			},
			{
				x: width * .78,
				y: height * .7,
				radius: unit * .016
			}
		], palette.ink, .75), {
			delay: .22,
			span: .5
		});
	};
	const haloBurst = (ctx) => {
		const { width, height, palette } = ctx;
		const unit = Math.min(width, height);
		const cx = width * (.44 + visualizerHash01(ctx.seed, 13, 103) * .12);
		const cy = height * .5;
		const reach = Math.hypot(width, height) * .7;
		const wedges = 14;
		addField$2(ctx, palette.tone1);
		for (let index = 0; index < wedges; index += 2) {
			const angle = index / wedges * Math.PI * 2 + visualizerHash01(ctx.seed, index, 107) * .09;
			const spread = .11;
			ctx.add(drawPolygonFill(ctx.pixi, [
				cx,
				cy,
				cx + Math.cos(angle - spread) * reach,
				cy + Math.sin(angle - spread) * reach,
				cx + Math.cos(angle + spread) * reach,
				cy + Math.sin(angle + spread) * reach
			], palette.tone3, .55, ctx.gradient), {
				delay: .04 + index * .012,
				span: .6
			});
		}
		ctx.add(drawPolygonFill(ctx.pixi, ellipsePolygon(cx, cy, unit * .34, unit * .34, 48), palette.paper, .94, ctx.gradient), {
			delay: .14,
			span: .55
		});
		[[.34, 3], [.44, 1.4]].forEach(([scale, stroke], index) => {
			ctx.add(drawRings(ctx.pixi, [{
				x: cx,
				y: cy,
				radius: unit * scale
			}], palette.ink, stroke, .75), {
				delay: .2 + index * .06,
				span: .5,
				drift: true
			});
		});
	};
	const TEMPERA_CHARM_COMPOSITIONS = {
		"bubble-drift": bubbleDrift,
		"cloud-window": cloudWindow,
		"heart-burst": heartBurst,
		"sparkle-field": sparkleField,
		"petal-arc": petalArc,
		"scallop-band": scallopBand,
		"ribbon-loop": ribbonLoop,
		"round-plate": roundPlate,
		"halo-burst": haloBurst
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/tempera/compositions/temperaCutout.ts
/** The full-bleed rectangle every composition starts from. */
	const fieldPolygon = (ctx) => rectPolygon(-ctx.bleed, -ctx.bleed, ctx.width + ctx.bleed * 2, ctx.height + ctx.bleed * 2);
	/** Full-bleed tone with `holes` punched through it. Hole outlines are separate nodes. */
	const addCutField = (ctx, color, holes, options = {}) => {
		ctx.add(drawPolygonFillWithHoles(ctx.pixi, fieldPolygon(ctx), holes, color, options.alpha ?? .94, ctx.gradient), {
			delay: options.delay ?? 0,
			span: options.span ?? .5
		});
	};
	/**
	* The ink lip around a hole. It is what keeps a cutout reading as a punched plate rather than
	* as a gap in the render, and it gives the inversion filter an edge next to the opening.
	*/
	const addHoleLip = (ctx, hole, width = 2.4, alpha = .85, delay = .1) => {
		ctx.add(drawPolygonOutline(ctx.pixi, hole, ctx.palette.ink, width, alpha), {
			delay,
			span: .5
		});
	};
	/** A rectangle centred on (cx, cy), `length` along `angle` and `width` across it. */
	const axisRect = (cx, cy, length, width, angle) => rotatePolygon(rectPolygon(cx - length / 2, cy - width / 2, length, width), cx, cy, angle);
	/**
	* The flow angle, pulled halfway back toward its own vertical axis.
	*
	* A channel has to be parallel to the travel or the hand-off stops reading as one continuous
	* corridor - but a channel is also long, so the raw tilt (up to ~14°) walks its ends sideways
	* by around 90px on a 720px frame, which is enough to slide an opening under the type. Half
	* the tilt keeps the direction and halves the walk; the residual mismatch over one hand-off
	* slide is a handful of pixels and invisible.
	*/
	const channelAxis = (ctx) => {
		const axis = (Math.sin(ctx.flowAngle) >= 0 ? 1 : -1) * Math.PI / 2;
		return axis + (ctx.flowAngle - axis) * .5;
	};
	/**
	* Unit vector across the channel axis, always pointing into the +x half of the frame.
	*
	* Flow is vertical-dominant but points up in some shots and down in others, so the raw
	* perpendicular flips sides between shots. Layout regions are fixed data, so a channel offset
	* built on the raw perpendicular would sometimes land under the type. Normalising the sign
	* keeps "offset by +n" meaning "to the right" in every shot.
	*/
	const acrossFlow = (angle) => {
		const across = angle + Math.PI / 2;
		const sign = Math.cos(across) >= 0 ? 1 : -1;
		return {
			x: Math.cos(across) * sign,
			y: Math.sin(across) * sign
		};
	};
	/** Centre of a channel offset sideways from the frame centre by `offset` across the axis. */
	const acrossPoint = (ctx, offset) => {
		const across = acrossFlow(channelAxis(ctx));
		return {
			x: ctx.width / 2 + across.x * offset,
			y: ctx.height / 2 + across.y * offset
		};
	};
	/** A point stepped `distance` along the axis from the frame centre, offset `offset` across it. */
	const flowPoint = (ctx, distance, offset) => {
		const base = acrossPoint(ctx, offset);
		const angle = channelAxis(ctx);
		return {
			x: base.x + Math.cos(angle) * distance,
			y: base.y + Math.sin(angle) * distance
		};
	};
	/** How far a shape must run to leave the frame on both ends whatever the flow angle. */
	const flowSpan = (ctx) => Math.hypot(ctx.width, ctx.height) * 1.6;

//#endregion
//#region ../../web/folia-major/src/components/visualizer/tempera/compositions/temperaApertureCompositions.ts
	const irisHole = (ctx) => {
		const { width, height, palette } = ctx;
		const unit = Math.min(width, height);
		const cx = width * .68;
		const cy = height * .42;
		const radius = unit * .3;
		const hole = circlePolygon(cx, cy, radius, 56);
		addCutField(ctx, palette.tone3, [hole]);
		addHoleLip(ctx, hole, 3, .9, .08);
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-ctx.bleed, cy - unit * .035, cx - radius * .7 + ctx.bleed, unit * .07), palette.tone4, .9, ctx.gradient), {
			delay: .14,
			span: .55,
			enterDX: -width * .12
		});
		if (!ctx.showDecor) return;
		ctx.add(drawRings(ctx.pixi, [{
			x: cx,
			y: cy,
			radius: radius * 1.14
		}], palette.tone4, 1.4, .6), {
			delay: .2,
			span: .5,
			drift: true
		});
	};
	const slotRail = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const top = height * .22;
		const slot = rectPolygon(-bleed, top, width + bleed * 2, height * .16);
		addCutField(ctx, palette.tone4, [slot]);
		addHoleLip(ctx, slot, 2.4, .8, .1);
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(width * .66, top - height * .06, width * .12, height * .28), palette.tone2, .95, ctx.gradient), {
			delay: .16,
			span: .55,
			enterDY: -height * .1
		});
		ctx.add(drawLines(ctx.pixi, [{
			x1: -bleed,
			y1: top + height * .22,
			x2: width + bleed,
			y2: top + height * .22
		}], palette.tone2, 1.4, .55), {
			delay: .22,
			span: .5,
			enterDX: width * .14
		});
	};
	const punchRow = (ctx) => {
		const { width, height, palette } = ctx;
		const radius = Math.min(width, height) * .028;
		const holes = [];
		for (let index = 0; index < 9; index += 1) {
			const x = width * (.08 + index * .105);
			holes.push(circlePolygon(x, height * .14, radius, 20));
			holes.push(circlePolygon(x, height * .86, radius, 20));
		}
		addCutField(ctx, palette.tone3, holes);
		[.22, .78].forEach((y, index) => {
			ctx.add(drawLines(ctx.pixi, [{
				x1: 0,
				y1: height * y,
				x2: width,
				y2: height * y
			}], palette.tone4, 1.6, .6), {
				delay: .12 + index * .04,
				span: .5,
				enterDX: (index === 0 ? 1 : -1) * width * .12
			});
		});
	};
	const filmGate = (ctx) => {
		const { width, height, palette } = ctx;
		const gate = rectPolygon(width * .2, height * .1, width * .6, height * .52);
		const holes = [gate];
		for (let index = 0; index < 4; index += 1) {
			const y = height * (.14 + index * .13);
			holes.push(rectPolygon(width * .08, y, width * .05, height * .06));
			holes.push(rectPolygon(width * .87, y, width * .05, height * .06));
		}
		addCutField(ctx, palette.tone4, holes);
		addHoleLip(ctx, gate, 3, .9, .08);
		ctx.add(drawHatchFill(ctx.pixi, rectPolygon(width * .2, height * .68, width * .6, height * .1), buildHatchSpec(ctx.seed, 131, 1.2), palette.tone2, .5), {
			delay: .18,
			span: .6,
			grow: true
		});
	};
	const crossVent = (ctx) => {
		const { width, height, palette } = ctx;
		const cx = width * .5;
		const cy = height * .38;
		const arm = Math.min(width, height) * .3;
		const bar = Math.min(width, height) * .1;
		const cross = [
			cx - bar,
			cy - arm,
			cx + bar,
			cy - arm,
			cx + bar,
			cy - bar,
			cx + arm,
			cy - bar,
			cx + arm,
			cy + bar,
			cx + bar,
			cy + bar,
			cx + bar,
			cy + arm,
			cx - bar,
			cy + arm,
			cx - bar,
			cy + bar,
			cx - arm,
			cy + bar,
			cx - arm,
			cy - bar,
			cx - bar,
			cy - bar
		];
		addCutField(ctx, palette.tone3, [cross]);
		addHoleLip(ctx, cross, 2.6, .85, .1);
		if (!ctx.showDecor) return;
		ctx.add(drawRings(ctx.pixi, [{
			x: cx,
			y: cy,
			radius: arm * 1.24
		}], palette.tone4, 1.2, .5), {
			delay: .2,
			span: .5
		});
	};
	const louvre = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const holes = [];
		for (let index = 0; index < 6; index += 1) {
			const y = height * (.08 + index * .1);
			const lean = height * .03;
			const thickness = height * (.05 - index * .004);
			holes.push([
				-bleed,
				y,
				width + bleed,
				y - lean,
				width + bleed,
				y - lean + thickness,
				-bleed,
				y + thickness
			]);
		}
		addCutField(ctx, palette.tone4, holes);
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(width * .1, height * .72, width * .08, height * .2), palette.tone2, .9, ctx.gradient), {
			delay: .2,
			span: .55,
			enterDY: height * .1
		});
	};
	const ringEye = (ctx) => {
		const { width, height, palette } = ctx;
		const unit = Math.min(width, height);
		const cx = width * .5;
		const cy = height * .5;
		const inner = unit * .31;
		const outer = unit * .44;
		const segments = 12;
		const gap = .03;
		const holes = Array.from({ length: segments }, (_, index) => {
			const start = index / segments * Math.PI * 2 + gap;
			const end = (index + 1) / segments * Math.PI * 2 - gap;
			return annularSectorPolygon(cx, cy, inner, outer, start, end, 8);
		});
		addCutField(ctx, palette.tone3, holes);
		ctx.add(drawRings(ctx.pixi, [{
			x: cx,
			y: cy,
			radius: inner
		}, {
			x: cx,
			y: cy,
			radius: outer
		}], palette.ink, 2.4, .85), {
			delay: .12,
			span: .5
		});
	};
	const notchStack = (ctx) => {
		const { width, height, palette } = ctx;
		const unit = Math.min(width, height);
		const holes = [];
		for (let index = 0; index < 4; index += 1) {
			const size = unit * (.26 - index * .05);
			holes.push(rectPolygon(width * (.6 + index * .06), height * (.1 + index * .19), size, size));
		}
		addCutField(ctx, palette.tone4, holes);
		holes.forEach((hole, index) => addHoleLip(ctx, hole, 2, .75, .1 + index * .04));
	};
	const wedgeGap = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const apexX = width * (.4 + visualizerHash01(ctx.seed, 3, 137) * .2);
		const wedge = [
			width * .1,
			-bleed,
			width * .92,
			-bleed,
			apexX,
			height * .56
		];
		addCutField(ctx, palette.tone3, [wedge]);
		addHoleLip(ctx, wedge, 3, .9, .08);
		ctx.add(drawLines(ctx.pixi, [{
			x1: apexX,
			y1: height * .58,
			x2: apexX,
			y2: height + bleed
		}], palette.tone4, 1.6, .6), {
			delay: .18,
			span: .5,
			enterDY: height * .12
		});
	};
	const dotSieve = (ctx) => {
		const { width, height, palette } = ctx;
		const unit = Math.min(width, height);
		const holes = [];
		for (let row = 0; row < 6; row += 1) for (let column = 0; column < 5; column += 1) {
			const density = 1 - column / 4.5;
			if (visualizerHash01(ctx.seed, row * 9 + column, 139) > density) continue;
			const x = width * (.06 + column * .105) + (row % 2 === 0 ? 0 : width * .05);
			holes.push(circlePolygon(x, height * (.1 + row * .16), unit * .045 * density + unit * .012, 18));
		}
		addCutField(ctx, palette.tone2, holes, { alpha: .92 });
	};
	const TEMPERA_APERTURE_COMPOSITIONS = {
		"iris-hole": irisHole,
		"slot-rail": slotRail,
		"punch-row": punchRow,
		"film-gate": filmGate,
		"cross-vent": crossVent,
		"louvre-slats": louvre,
		"ring-eye": ringEye,
		"notch-stack": notchStack,
		"wedge-gap": wedgeGap,
		"dot-sieve": dotSieve
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/tempera/compositions/temperaSignalCompositions.ts
	const addField$1 = (ctx, color, alpha = .92) => {
		const { width, height, bleed } = ctx;
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, height + bleed * 2), color, alpha, ctx.gradient), { span: .5 });
	};
	const sightMark = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const cx = width * .5;
		const cy = height * .5;
		addField$1(ctx, palette.tone1);
		ctx.add(drawLines(ctx.pixi, [{
			x1: -bleed,
			y1: cy,
			x2: width + bleed,
			y2: cy
		}, {
			x1: cx,
			y1: -bleed,
			x2: cx,
			y2: height + bleed
		}], palette.tone4, 1.4, .6), {
			delay: .04,
			span: .55,
			enterDX: width * .1
		});
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(width * .24, height * .36, width * .52, height * .28), palette.tone3, .94, ctx.gradient), {
			delay: .1,
			span: .55,
			enterDY: height * .06
		});
		ctx.add(drawPolygonOutline(ctx.pixi, rectPolygon(width * .2, height * .32, width * .6, height * .36), palette.ink, 2.4, .85), {
			delay: .16,
			span: .5
		});
		if (!ctx.showDecor) return;
		[
			[
				.2,
				.32,
				1,
				1
			],
			[
				.8,
				.32,
				-1,
				1
			],
			[
				.2,
				.68,
				1,
				-1
			],
			[
				.8,
				.68,
				-1,
				-1
			]
		].forEach(([fx, fy, sx, sy], index) => {
			const x = width * fx;
			const y = height * fy;
			ctx.add(drawLines(ctx.pixi, [{
				x1: x,
				y1: y,
				x2: x + sx * width * .04,
				y2: y
			}, {
				x1: x,
				y1: y,
				x2: x,
				y2: y + sy * height * .05
			}], palette.ink, 3, .8), {
				delay: .2 + index * .03,
				span: .5
			});
		});
	};
	const dialScale = (ctx) => {
		const { width, height, palette } = ctx;
		const unit = Math.min(width, height);
		const cx = width * .5;
		const cy = height * .5;
		addField$1(ctx, palette.tone2);
		ctx.add(drawPolygonFill(ctx.pixi, annularSectorPolygon(cx, cy, unit * .34, unit * .44, 0, Math.PI * 2 - .006, 60), palette.tone4, .92, ctx.gradient), {
			delay: .05,
			span: .6
		});
		const ticks = Array.from({ length: 24 }, (_, index) => {
			const angle = index / 24 * Math.PI * 2;
			const inner = unit * (index % 6 === 0 ? .46 : .475);
			return {
				x1: cx + Math.cos(angle) * inner,
				y1: cy + Math.sin(angle) * inner,
				x2: cx + Math.cos(angle) * unit * .5,
				y2: cy + Math.sin(angle) * unit * .5
			};
		});
		ctx.add(drawLines(ctx.pixi, ticks, palette.ink, 1.6, .7), {
			delay: .14,
			span: .55
		});
		const pointer = .4 + visualizerHash01(ctx.seed, 5, 149) * .5;
		ctx.add(drawPolygonFill(ctx.pixi, axisRect(cx + Math.cos(pointer * Math.PI * 2) * unit * .2, cy + Math.sin(pointer * Math.PI * 2) * unit * .2, unit * .4, unit * .035, pointer * Math.PI * 2), palette.ink, .9), {
			delay: .2,
			span: .5
		});
	};
	const chevronRun = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const focus = 2;
		addField$1(ctx, palette.tone1);
		for (let index = 0; index < 5; index += 1) {
			const y = height * (.02 + index * .24);
			const rise = height * .12;
			const thickness = height * .07;
			const chevron = [
				-bleed,
				y,
				width * .5,
				y - rise,
				width + bleed,
				y,
				width + bleed,
				y + thickness,
				width * .5,
				y - rise + thickness,
				-bleed,
				y + thickness
			];
			ctx.add(drawPolygonFill(ctx.pixi, chevron, index === focus ? palette.tone4 : palette.tone3, index === focus ? .95 : .7, ctx.gradient), {
				delay: index * .04,
				span: .55,
				enterDY: -height * .12
			});
		}
	};
	const tallyColumn = (ctx) => {
		const { width, height, palette } = ctx;
		const x = width * .82;
		addField$1(ctx, palette.tone1);
		const marks = Array.from({ length: 11 }, (_, index) => {
			const long = index % 4 === 0;
			return {
				x1: x,
				y1: height * (.1 + index * .075),
				x2: x + width * (long ? .12 : .06),
				y2: height * (.1 + index * .075)
			};
		});
		ctx.add(drawLines(ctx.pixi, marks, palette.tone4, 2.4, .75), {
			delay: .06,
			span: .6,
			enterDX: width * .1
		});
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(x - width * .02, height * .4, width * .16, height * .12), palette.tone4, .95, ctx.gradient), {
			delay: .16,
			span: .55,
			enterDX: width * .14
		});
		ctx.add(drawLines(ctx.pixi, [{
			x1: x,
			y1: height * .06,
			x2: x,
			y2: height * .92
		}], palette.ink, 1.6, .6), {
			delay: .2,
			span: .5
		});
	};
	const gridFocus = (ctx) => {
		const { width, height, palette } = ctx;
		const left = width * .08;
		const top = height * .12;
		const cellWidth = width * .84 / 4;
		const cellHeight = height * .76 / 3;
		const cell = (column, row) => rectPolygon(left + cellWidth * column, top + cellHeight * row, cellWidth, cellHeight);
		addCutField(ctx, palette.tone2, [cell(0, 0), cell(3, 2)]);
		addHoleLip(ctx, cell(0, 0), 2, .7, .1);
		addHoleLip(ctx, cell(3, 2), 2, .7, .14);
		ctx.add(drawPolygonFill(ctx.pixi, cell(3, 0), palette.tone4, .95, ctx.gradient), {
			delay: .08,
			span: .55,
			enterDX: width * .08
		});
		const lines = [];
		for (let column = 0; column <= 4; column += 1) lines.push({
			x1: left + cellWidth * column,
			y1: top,
			x2: left + cellWidth * column,
			y2: top + cellHeight * 3
		});
		for (let row = 0; row <= 3; row += 1) lines.push({
			x1: left,
			y1: top + cellHeight * row,
			x2: left + cellWidth * 4,
			y2: top + cellHeight * row
		});
		ctx.add(drawLines(ctx.pixi, lines, palette.tone4, 1.2, .6), {
			delay: .16,
			span: .55
		});
	};
	const axisCaps = (ctx) => {
		const { width, height, palette } = ctx;
		const unit = Math.min(width, height);
		const cx = width * .5;
		const cy = height * .42;
		const bar = axisRect(cx, cy, flowSpan(ctx), unit * .22, ctx.flowAngle);
		const port = circlePolygon(cx, cy, unit * .08, 32);
		addCutField(ctx, palette.tone1, [port], { alpha: .92 });
		ctx.add(drawPolygonFillWithHoles(ctx.pixi, bar, [port], palette.tone4, .94, ctx.gradient), {
			delay: .05,
			span: .6
		});
		ctx.add(drawRings(ctx.pixi, [{
			x: cx,
			y: cy,
			radius: unit * .08
		}], palette.ink, 2.4, .85), {
			delay: .12,
			span: .5
		});
		[1, -1].forEach((side, index) => {
			const distance = unit * .46;
			const capX = cx + Math.cos(ctx.flowAngle) * distance * side;
			const capY = cy + Math.sin(ctx.flowAngle) * distance * side;
			ctx.add(drawPolygonFill(ctx.pixi, axisRect(capX, capY, unit * .1, unit * .34, ctx.flowAngle), palette.ink, .85), {
				delay: .16 + index * .04,
				span: .5
			});
		});
	};
	const strobeSlats = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		addField$1(ctx, palette.tone1);
		const widths = [
			.06,
			.03,
			.09,
			.04,
			.34,
			.04,
			.08,
			.03,
			.06
		];
		let cursor = width * .03;
		widths.forEach((fraction, index) => {
			const slat = rectPolygon(cursor, -bleed, width * fraction, height + bleed * 2);
			const wide = fraction > .2;
			ctx.add(drawPolygonFill(ctx.pixi, slat, wide ? palette.tone2 : palette.tone4, wide ? .9 : .85, ctx.gradient), {
				delay: index * .03,
				span: .55,
				enterDY: (index % 2 === 0 ? 1 : -1) * height * .14
			});
			cursor += width * (fraction + .02);
		});
	};
	const offsetPlate = (ctx) => {
		const { width, height, palette } = ctx;
		const unit = Math.min(width, height);
		const step = unit * .06;
		const holeX = width * .72;
		const holeY = height * .36;
		const hole = circlePolygon(holeX, holeY, unit * .05, 28);
		addCutField(ctx, palette.tone1, [hole], { alpha: .9 });
		[
			palette.tone2,
			palette.tone3,
			palette.tone4
		].forEach((tone, index) => {
			const plate = rectPolygon(width * .2 + step * index, height * .2 + step * index, width * .5, height * .5);
			ctx.add(drawPolygonFillWithHoles(ctx.pixi, plate, [hole], tone, index === 2 ? .95 : .8, ctx.gradient), {
				delay: index * .06,
				span: .55,
				enterDX: -step * 2,
				enterDY: -step
			});
		});
		ctx.add(drawRings(ctx.pixi, [{
			x: holeX,
			y: holeY,
			radius: unit * .05
		}], palette.ink, 2, .8), {
			delay: .22,
			span: .5
		});
	};
	const radialComb = (ctx) => {
		const { width, height, palette } = ctx;
		const hubX = width * 1.06;
		const hubY = height * 1.1;
		const reach = Math.hypot(width, height) * 1.3;
		addField$1(ctx, palette.tone2);
		const rays = Array.from({ length: 11 }, (_, index) => {
			const angle = Math.PI + .16 + index * .062;
			return {
				x1: hubX,
				y1: hubY,
				x2: hubX + Math.cos(angle) * reach,
				y2: hubY + Math.sin(angle) * reach
			};
		});
		ctx.add(drawLines(ctx.pixi, rays, palette.tone4, 2.6, .8), {
			delay: .06,
			span: .6
		});
		const start = Math.PI + .44;
		const end = Math.PI + .56;
		ctx.add(drawPolygonFill(ctx.pixi, [
			hubX,
			hubY,
			hubX + Math.cos(start) * reach,
			hubY + Math.sin(start) * reach,
			hubX + Math.cos(end) * reach,
			hubY + Math.sin(end) * reach
		], palette.tone4, .95, ctx.gradient), {
			delay: .14,
			span: .6
		});
		if (!ctx.showDecor) return;
		ctx.add(drawHatchFill(ctx.pixi, rectPolygon(width * .06, height * .08, width * .2, height * .14), buildHatchSpec(ctx.seed, 151), palette.tone4, .4), {
			delay: .24,
			span: .6,
			grow: true
		});
	};
	const bracketTarget = (ctx) => {
		const { width, height, palette } = ctx;
		const box = rectPolygon(width * .28, height * .54, width * .44, height * .34);
		addCutField(ctx, palette.tone3, [box]);
		addHoleLip(ctx, box, 2.4, .85, .08);
		const arm = Math.min(width, height) * .08;
		[
			[
				.28,
				.54,
				1,
				1
			],
			[
				.72,
				.54,
				-1,
				1
			],
			[
				.28,
				.88,
				1,
				-1
			],
			[
				.72,
				.88,
				-1,
				-1
			]
		].forEach(([fx, fy, sx, sy], index) => {
			const x = width * fx + sx * width * .03;
			const y = height * fy + sy * height * .04;
			ctx.add(drawLines(ctx.pixi, [{
				x1: x,
				y1: y,
				x2: x + sx * arm,
				y2: y
			}, {
				x1: x,
				y1: y,
				x2: x,
				y2: y + sy * arm
			}], palette.ink, 3.5, .9), {
				delay: .12 + index * .04,
				span: .5,
				enterDX: sx * width * .05
			});
		});
		ctx.add(drawLines(ctx.pixi, [{
			x1: width * .1,
			y1: height * .44,
			x2: width * .9,
			y2: height * .44
		}], palette.tone4, 1.6, .6), {
			delay: .24,
			span: .5,
			enterDX: -width * .1
		});
	};
	const TEMPERA_SIGNAL_COMPOSITIONS = {
		"sight-mark": sightMark,
		"dial-scale": dialScale,
		"chevron-run": chevronRun,
		"tally-column": tallyColumn,
		"grid-focus": gridFocus,
		"axis-caps": axisCaps,
		"strobe-slats": strobeSlats,
		"offset-plate": offsetPlate,
		"radial-comb": radialComb,
		"bracket-target": bracketTarget
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/tempera/compositions/temperaCorridorCompositions.ts
	const channel = (ctx, offset, width) => {
		const centre = acrossPoint(ctx, offset);
		return axisRect(centre.x, centre.y, flowSpan(ctx), width, channelAxis(ctx));
	};
	/** Rails running the length of a channel, a hair outside its lip. */
	const addRails = (ctx, offset, width, alpha = .55) => {
		const angle = channelAxis(ctx);
		const half = flowSpan(ctx) / 2;
		[-1, 1].forEach((side, index) => {
			const start = flowPoint(ctx, -half, offset + side * width);
			const end = flowPoint(ctx, half, offset + side * width);
			ctx.add(drawLines(ctx.pixi, [{
				x1: start.x,
				y1: start.y,
				x2: end.x,
				y2: end.y
			}], ctx.palette.tone4, 1.4, alpha), {
				delay: .18 + index * .03,
				span: .5,
				enterDX: Math.cos(angle) * ctx.width * .08,
				enterDY: Math.sin(angle) * ctx.height * .08
			});
		});
	};
	const flowChannel = (ctx) => {
		const { width, palette } = ctx;
		const unit = Math.min(width, ctx.height);
		const slot = channel(ctx, width * .26, unit * .3);
		addCutField(ctx, palette.tone3, [slot]);
		addHoleLip(ctx, slot, 2.6, .85, .08);
		addRails(ctx, width * .26, unit * .19);
	};
	const twinChannel = (ctx) => {
		const { width, palette } = ctx;
		const unit = Math.min(width, ctx.height);
		const slots = [channel(ctx, -width * .34, unit * .16), channel(ctx, width * .34, unit * .16)];
		addCutField(ctx, palette.tone4, slots);
		slots.forEach((slot, index) => addHoleLip(ctx, slot, 2.4, .8, .1 + index * .04));
	};
	const reedRun = (ctx) => {
		const { width, palette } = ctx;
		const unit = Math.min(width, ctx.height);
		const slots = [];
		[-1, 1].forEach((side) => {
			for (let index = 0; index < 4; index += 1) slots.push(channel(ctx, side * width * (.22 + index * .08), unit * .035));
		});
		addCutField(ctx, palette.tone2, slots, { alpha: .9 });
		addRails(ctx, 0, width * .17, .4);
	};
	const taperChannel = (ctx) => {
		const { width, palette } = ctx;
		const unit = Math.min(width, ctx.height);
		const half = flowSpan(ctx) / 2;
		const wide = unit * .24;
		const narrow = unit * .06;
		const offset = width * .24;
		const slot = [
			flowPoint(ctx, -half, offset - wide),
			flowPoint(ctx, -half, offset + wide),
			flowPoint(ctx, half, offset + narrow),
			flowPoint(ctx, half, offset - narrow)
		].flatMap((point) => [point.x, point.y]);
		addCutField(ctx, palette.tone3, [slot]);
		addHoleLip(ctx, slot, 2.6, .85, .08);
	};
	const chainPorts = (ctx) => {
		const { width, height, palette } = ctx;
		const unit = Math.min(width, height);
		const offset = width * .28;
		const spacing = height * .26;
		const ports = [
			-1.5,
			-.5,
			.5,
			1.5
		].map((step) => flowPoint(ctx, step * spacing, offset));
		addCutField(ctx, palette.tone4, ports.map((port) => circlePolygon(port.x, port.y, unit * .11, 40)));
		ctx.add(drawRings(ctx.pixi, ports.map((port) => ({
			x: port.x,
			y: port.y,
			radius: unit * .11
		})), palette.ink, 2.4, .85), {
			delay: .1,
			span: .55
		});
		const head = flowPoint(ctx, -spacing * 2.4, offset);
		const tail = flowPoint(ctx, spacing * 2.4, offset);
		ctx.add(drawLines(ctx.pixi, [{
			x1: head.x,
			y1: head.y,
			x2: tail.x,
			y2: tail.y
		}], palette.tone2, 1.6, .6), {
			delay: .18,
			span: .5
		});
	};
	const dashChannel = (ctx) => {
		const { width, height, palette } = ctx;
		const unit = Math.min(width, height);
		const offset = width * .24;
		const step = height * .32;
		const angle = channelAxis(ctx);
		const slots = [
			-1.5,
			-.5,
			.5,
			1.5
		].map((index) => {
			const centre = flowPoint(ctx, index * step, offset);
			return axisRect(centre.x, centre.y, step * .72, unit * .22, angle);
		});
		addCutField(ctx, palette.tone3, slots);
		slots.forEach((slot, index) => addHoleLip(ctx, slot, 2.2, .8, .08 + index * .03));
	};
	const windowRun = (ctx) => {
		const { width, height, palette } = ctx;
		const unit = Math.min(width, height);
		const offset = width * .27;
		const step = height * .36;
		const angle = channelAxis(ctx);
		const slots = [
			-1.5,
			-.5,
			.5,
			1.5
		].map((index) => {
			const centre = flowPoint(ctx, index * step, offset);
			const size = unit * .2;
			return rotatePolygon(roundedRectPolygon(centre.x - size, centre.y - size * .62, size * 2, size * 1.24, size * .3), centre.x, centre.y, angle + Math.PI / 2);
		});
		addCutField(ctx, palette.tone4, slots);
		slots.forEach((slot, index) => addHoleLip(ctx, slot, 2.4, .85, .08 + index * .03));
	};
	const bridgeSpan = (ctx) => {
		const { width, height, palette } = ctx;
		const slot = channel(ctx, 0, Math.min(width, height) * .62);
		addCutField(ctx, palette.tone4, [slot]);
		addHoleLip(ctx, slot, 2.6, .8, .08);
		const band = axisRect(width / 2, height / 2, flowSpan(ctx), height * .46, channelAxis(ctx) + Math.PI / 2);
		ctx.add(drawPolygonFill(ctx.pixi, band, palette.tone3, .95, ctx.gradient), {
			delay: .12,
			span: .6,
			enterDX: -width * .16
		});
		ctx.add(drawPolygonOutline(ctx.pixi, band, palette.ink, 2, .7), {
			delay: .2,
			span: .5,
			enterDX: width * .1
		});
	};
	const braidChannel = (ctx) => {
		const { width, height, palette } = ctx;
		const unit = Math.min(width, height);
		const angle = channelAxis(ctx);
		const pitch = height * .42;
		const slots = [];
		[-1, 1].forEach((side) => {
			for (let index = -1; index <= 1; index += 1) {
				const centre = flowPoint(ctx, index * pitch + side * pitch * .5, side * width * .32);
				slots.push(axisRect(centre.x, centre.y, pitch * .78, unit * .16, angle));
			}
		});
		addCutField(ctx, palette.tone3, slots);
		slots.forEach((slot, index) => addHoleLip(ctx, slot, 2.2, .8, .08 + index * .025));
	};
	const portLadder = (ctx) => {
		const { width, height, palette } = ctx;
		const unit = Math.min(width, height);
		const offset = width * .3;
		const slot = channel(ctx, offset, unit * .14);
		addCutField(ctx, palette.tone2, [slot]);
		addHoleLip(ctx, slot, 2.2, .8, .08);
		const rungs = Array.from({ length: 9 }, (_, index) => {
			const distance = (index - 4) * height * .13;
			const long = index % 3 === 0;
			const inner = flowPoint(ctx, distance, offset - unit * .09);
			const outer = flowPoint(ctx, distance, offset - unit * (long ? .22 : .15));
			return {
				x1: inner.x,
				y1: inner.y,
				x2: outer.x,
				y2: outer.y
			};
		});
		ctx.add(drawLines(ctx.pixi, rungs, palette.tone4, 2, .7), {
			delay: .14,
			span: .6
		});
	};
	const TEMPERA_CORRIDOR_COMPOSITIONS = {
		"flow-channel": flowChannel,
		"twin-channel": twinChannel,
		"reed-run": reedRun,
		"taper-channel": taperChannel,
		"chain-ports": chainPorts,
		"dash-channel": dashChannel,
		"window-run": windowRun,
		"bridge-span": bridgeSpan,
		"braid-channel": braidChannel,
		"port-ladder": portLadder
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/tempera/compositions/temperaMonolithKit.ts
	const addMass = (ctx, polygon, color, options = {}) => {
		ctx.add(drawPolygonFill(ctx.pixi, polygon, color, options.alpha ?? .95, ctx.gradient), {
			delay: options.delay ?? .04,
			span: options.span ?? .6,
			enterDX: options.enterDX ?? 0,
			enterDY: options.enterDY ?? 0
		});
		if (options.edge === false) return;
		ctx.add(drawPolygonOutline(ctx.pixi, polygon, ctx.palette.ink, options.edgeWidth ?? 2.4, .8), {
			delay: (options.delay ?? .04) + .06,
			span: .5,
			enterDX: (options.enterDX ?? 0) * .6,
			enterDY: (options.enterDY ?? 0) * .6
		});
	};
	/** The full-bleed ground the mass stands on. */
	const addGround = (ctx, color, alpha = .94) => {
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-ctx.bleed, -ctx.bleed, ctx.width + ctx.bleed * 2, ctx.height + ctx.bleed * 2), color, alpha, ctx.gradient), { span: .5 });
	};
	/**
	* Ruled stripes over one face of a mass. The polygon has to be convex - `buildHatchLines`
	* clips against edge half-planes - so callers pass a convex slice of the mass rather than the
	* mass itself, which is usually stepped or notched.
	*/
	const addFaceRuling = (ctx, face, salt, color = ctx.palette.tone4, alpha = .5) => {
		ctx.add(drawHatchFill(ctx.pixi, face, buildHatchSpec(ctx.seed, salt, .7), color, alpha), {
			delay: .16,
			span: .65,
			grow: true
		});
	};
	/**
	* Two or three hairlines ruled across the entire picture at shallow angles. They are the only
	* thing in these compositions that ignores the mass, and that is what gives the mass its
	* scale: a line that crosses everything without touching anything.
	*/
	const addSurveyLines = (ctx, count = 3, salt = 211) => {
		const { width, height, bleed } = ctx;
		const lines = Array.from({ length: Math.max(1, count) }, (_, index) => {
			const anchor = height * (.16 + (index * .31 + ctx.seed % 7 * .04) % .7);
			const lean = height * (index % 2 === 0 ? .22 : -.16);
			return {
				x1: -bleed,
				y1: anchor - lean,
				x2: width + bleed,
				y2: anchor + lean
			};
		});
		ctx.add(drawLines(ctx.pixi, lines, ctx.palette.paper, 1, .45), {
			delay: .2 + salt % 3 * .02,
			span: .7,
			enterDX: width * .2
		});
	};
	/** Survey marks in two opposite corners: registration for something far too big to register. */
	const addCornerTicks = (ctx) => {
		const { width, height, palette } = ctx;
		const inset = Math.min(width, height) * .06;
		const arm = Math.min(width, height) * .05;
		[[1, 1], [-1, -1]].forEach(([sx, sy], index) => {
			const x = sx > 0 ? inset : width - inset;
			const y = sy > 0 ? inset : height - inset;
			ctx.add(drawLines(ctx.pixi, [{
				x1: x,
				y1: y,
				x2: x + sx * arm,
				y2: y
			}, {
				x1: x,
				y1: y,
				x2: x,
				y2: y + sy * arm
			}], palette.paper, 1.6, .6), {
				delay: .26 + index * .04,
				span: .5
			});
		});
	};
	/** A loose wire contour parked in a corner - a survey trace of a shape that is not there. */
	const addWireTrace = (ctx, cx, cy, radius) => {
		const group = ctx.createGroup(0, cx, cy);
		ctx.add(drawPolyline(ctx.pixi, buildScribblePath(ctx.decor.scribbleSeed, 199, 0, 0, radius, 1), ctx.palette.paper, 1.2, .4), {
			delay: .3,
			span: .6,
			drift: true
		}, group);
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/tempera/compositions/temperaMonolithCompositions.ts
	const apexMass = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const apexX = width * (.46 + visualizerHash01(ctx.seed, 3, 227) * .12);
		const apexY = height * .38;
		addGround(ctx, palette.tone1);
		const mass = [
			-bleed,
			height + bleed,
			apexX,
			apexY,
			width + bleed,
			height + bleed
		];
		addMass(ctx, mass, palette.tone3, { enterDY: height * .1 });
		addFaceRuling(ctx, [
			apexX,
			apexY,
			width + bleed,
			height + bleed,
			apexX + width * .24,
			height + bleed
		], 229);
		addSurveyLines(ctx, 3);
		if (!ctx.showDecor) return;
		addCornerTicks(ctx);
		addWireTrace(ctx, width * .12, height * .84, Math.min(width, height) * .09);
	};
	const ziggurat = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		addGround(ctx, palette.tone1);
		const tones = [
			palette.tone4,
			palette.tone3,
			palette.tone3,
			palette.tone2
		];
		for (let course = 0; course < 4; course += 1) {
			const half = width * (.44 - course * .09);
			const top = height * (.9 - course * .13) - height * .13;
			addMass(ctx, rectPolygon(width / 2 - half, top, half * 2, height * .13 + bleed), tones[course], {
				delay: .04 + course * .05,
				enterDY: height * .08,
				edgeWidth: 2
			});
		}
		addSurveyLines(ctx, 2);
		if (!ctx.showDecor) return;
		addCornerTicks(ctx);
	};
	const slabWall = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const edge = width * .36;
		addGround(ctx, palette.tone1);
		addMass(ctx, rectPolygon(edge, -bleed, width - edge + bleed, height + bleed * 2), palette.tone3, { edge: false });
		ctx.add(drawLines(ctx.pixi, [{
			x1: edge,
			y1: -bleed,
			x2: edge,
			y2: height + bleed
		}], palette.ink, 3.5, .9), {
			delay: .1,
			span: .55,
			enterDX: width * .06
		});
		addFaceRuling(ctx, rectPolygon(width * .62, height * .1, width * .3, height * .8), 233);
		addSurveyLines(ctx, 3);
		if (!ctx.showDecor) return;
		addWireTrace(ctx, width * .16, height * .24, Math.min(width, height) * .08);
	};
	const cantilever = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		addGround(ctx, palette.tone1);
		addMass(ctx, rectPolygon(.16 * width, height * .5, width * .1, height * .5 + bleed), palette.tone4, {
			delay: .02,
			enterDY: height * .1,
			edge: false
		});
		addMass(ctx, rectPolygon(-bleed, height * .34, width * .78 + bleed, height * .16), palette.tone3, {
			delay: .08,
			enterDX: -width * .14
		});
		addFaceRuling(ctx, rectPolygon(width * .3, height * .36, width * .44, height * .12), 239, palette.paper, .35);
		addSurveyLines(ctx, 2);
		if (!ctx.showDecor) return;
		addCornerTicks(ctx);
	};
	const pylonPair = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const bay = rectPolygon(width * .26, height * .3, width * .48, height * .7 + bleed);
		addCutField(ctx, palette.tone1, [bay], { alpha: .94 });
		addHoleLip(ctx, bay, 2, .6, .12);
		[.1, .74].forEach((x, index) => {
			addMass(ctx, rectPolygon(width * x, height * .28, width * .16, height * .72 + bleed), palette.tone4, {
				delay: .04 + index * .05,
				enterDY: height * .08,
				edgeWidth: 2
			});
		});
		addMass(ctx, rectPolygon(width * .04, height * .14, width * .92, height * .16), palette.tone3, {
			delay: .14,
			enterDY: -height * .1
		});
		addSurveyLines(ctx, 2);
	};
	const bunkerSlit = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const slit = rectPolygon(-bleed, height * .62, width + bleed * 2, height * .06);
		addCutField(ctx, palette.tone1, [slit], { alpha: .94 });
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, height * .34, width + bleed * 2, height * .28), palette.tone4, .95, ctx.gradient), {
			delay: .05,
			span: .6,
			enterDY: -height * .08
		});
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, height * .68, width + bleed * 2, height * .32 + bleed), palette.tone4, .95, ctx.gradient), {
			delay: .08,
			span: .6,
			enterDY: height * .1
		});
		ctx.add(drawLines(ctx.pixi, [{
			x1: -bleed,
			y1: height * .34,
			x2: width + bleed,
			y2: height * .34
		}], palette.ink, 3, .85), {
			delay: .14,
			span: .5
		});
		addFaceRuling(ctx, rectPolygon(width * .08, height * .72, width * .36, height * .2), 241, palette.paper, .3);
		addSurveyLines(ctx, 2);
	};
	const plinthStack = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		addGround(ctx, palette.tone1);
		addMass(ctx, rectPolygon(width * .06, height * .72, width * .88, height * .28 + bleed), palette.tone4, { edgeWidth: 2 });
		addMass(ctx, rectPolygon(width * .16, height * .52, width * .68, height * .2), palette.tone3, {
			delay: .08,
			edgeWidth: 2
		});
		addMass(ctx, rectPolygon(width * .28, height * .34, width * .44, height * .18), palette.tone2, {
			delay: .14,
			enterDY: height * .06
		});
		addFaceRuling(ctx, rectPolygon(width * .2, height * .76, width * .6, height * .16), 243);
		addSurveyLines(ctx, 3);
		if (!ctx.showDecor) return;
		addCornerTicks(ctx);
	};
	const buttressRun = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const band = rectPolygon(-bleed, height * .42, width + bleed * 2, height * .58 + bleed);
		addCutField(ctx, palette.tone1, [band], { alpha: .94 });
		for (let index = 0; index < 5; index += 1) {
			const left = width * (index * .2 + .01);
			addMass(ctx, [
				left,
				height + bleed,
				left + width * .09,
				height * .42,
				left + width * .17,
				height + bleed
			], index % 2 === 0 ? palette.tone4 : palette.tone3, {
				delay: .04 + index * .04,
				enterDY: height * .12,
				edgeWidth: 2
			});
		}
		ctx.add(drawLines(ctx.pixi, [{
			x1: -bleed,
			y1: height * .42,
			x2: width + bleed,
			y2: height * .42
		}], palette.ink, 2.4, .8), {
			delay: .2,
			span: .5,
			enterDX: -width * .1
		});
		addSurveyLines(ctx, 2);
	};
	const voidCore = (ctx) => {
		const { width, height, palette } = ctx;
		const core = rectPolygon(width * .22, height * .34, width * .56, height * .46);
		addCutField(ctx, palette.tone3, [core], { alpha: .95 });
		addHoleLip(ctx, core, 3.5, .9, .08);
		addFaceRuling(ctx, rectPolygon(width * .04, height * .36, width * .14, height * .42), 247, palette.tone4, .45);
		addSurveyLines(ctx, 3);
		if (!ctx.showDecor) return;
		addCornerTicks(ctx);
		addWireTrace(ctx, width * .86, height * .86, Math.min(width, height) * .08);
	};
	const shearBlock = (ctx) => {
		const { width, height, palette } = ctx;
		const left = width * .14;
		const right = width * .86;
		const top = height * .2;
		const bottom = height * .8;
		const cutTop = width * .56;
		const cutBottom = width * .44;
		addGround(ctx, palette.tone1);
		addMass(ctx, [
			left,
			top,
			cutTop,
			top,
			cutBottom,
			bottom,
			left,
			bottom
		], palette.tone3, {
			enterDX: -width * .08,
			enterDY: -height * .03,
			edgeWidth: 2
		});
		addMass(ctx, [
			cutTop + width * .03,
			top + height * .04,
			right,
			top + height * .04,
			right,
			bottom + height * .04,
			cutBottom + width * .03,
			bottom + height * .04
		], palette.tone4, {
			delay: .1,
			enterDX: width * .08,
			enterDY: height * .03,
			edgeWidth: 2
		});
		addSurveyLines(ctx, 2);
		if (!ctx.showDecor) return;
		ctx.add(drawPolygonOutline(ctx.pixi, rectPolygon(width * .1, height * .16, width * .8, height * .68), palette.paper, 1.2, .35), {
			delay: .28,
			span: .55
		});
	};
	const TEMPERA_MONOLITH_COMPOSITIONS = {
		"apex-mass": apexMass,
		"ziggurat": ziggurat,
		"slab-wall": slabWall,
		"cantilever": cantilever,
		"pylon-pair": pylonPair,
		"bunker-slit": bunkerSlit,
		"plinth-stack": plinthStack,
		"buttress-run": buttressRun,
		"void-core": voidCore,
		"shear-block": shearBlock
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/tempera/compositions/temperaTerrainCompositions.ts
	const ridgeLine = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const lift = visualizerHash01(ctx.seed, 5, 251) * .06;
		addGround(ctx, palette.tone1);
		addMass(ctx, [
			-bleed,
			height * (.68 + lift),
			width * .34,
			height * (.4 + lift),
			width * .62,
			height * (.58 + lift),
			width + bleed,
			height * (.34 + lift),
			width + bleed,
			height + bleed,
			-bleed,
			height + bleed
		], palette.tone3, { enterDY: height * .08 });
		addFaceRuling(ctx, [
			width * .34,
			height * (.42 + lift),
			width * .62,
			height * (.6 + lift),
			width * .62,
			height + bleed,
			width * .34,
			height + bleed
		], 253, palette.tone4, .45);
		addSurveyLines(ctx, 3);
		if (!ctx.showDecor) return;
		addCornerTicks(ctx);
		addWireTrace(ctx, width * .1, height * .86, Math.min(width, height) * .08);
	};
	const chasm = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const gap = rectPolygon(width * .46, -bleed, width * .1, height + bleed * 2);
		addCutField(ctx, palette.tone1, [gap], { alpha: .94 });
		addMass(ctx, rectPolygon(-bleed, height * .3, width * .46 + bleed, height * .7 + bleed), palette.tone3, {
			enterDX: -width * .1,
			edgeWidth: 2
		});
		addMass(ctx, rectPolygon(width * .56, height * .2, width * .44 + bleed, height * .8 + bleed), palette.tone4, {
			delay: .08,
			enterDX: width * .1,
			edgeWidth: 2
		});
		addSurveyLines(ctx, 2);
	};
	const overhang = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		addGround(ctx, palette.tone1);
		addMass(ctx, rectPolygon(-bleed, -bleed, width + bleed * 2, height * .34 + bleed), palette.tone4, {
			enterDY: -height * .1,
			edgeWidth: 3
		});
		addMass(ctx, rectPolygon(width * .72, height * .34, width * .28 + bleed, height * .66 + bleed), palette.tone3, {
			delay: .08,
			enterDX: width * .1,
			edge: false
		});
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, height * .34, width + bleed * 2, height * .14), palette.tone2, .5, ctx.gradient), {
			delay: .16,
			span: .6
		});
		addSurveyLines(ctx, 3);
		if (!ctx.showDecor) return;
		addCornerTicks(ctx);
	};
	const stepWell = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const shaft = rectPolygon(width * .36, height * .6, width * .28, height * .4 + bleed);
		addCutField(ctx, palette.tone2, [shaft], { alpha: .94 });
		[[
			.14,
			.3,
			.72,
			palette.tone3
		], [
			.24,
			.44,
			.52,
			palette.tone4
		]].forEach(([x, y, w, tone], index) => {
			ctx.add(drawPolygonFillWithHoles(ctx.pixi, rectPolygon(width * x, height * y, width * w, height * (1 - y) + bleed), [shaft], tone, .95, ctx.gradient), {
				delay: .06 + index * .06,
				span: .6,
				enterDY: height * .06
			});
		});
		addHoleLip(ctx, shaft, 3, .85, .18);
		addSurveyLines(ctx, 2);
	};
	const pierRow = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const water = rectPolygon(-bleed, height * .46, width + bleed * 2, height * .54 + bleed);
		addCutField(ctx, palette.tone1, [water], { alpha: .94 });
		[
			.04,
			.28,
			.52,
			.76
		].forEach((x, index) => {
			addMass(ctx, rectPolygon(width * x, height * .46, width * .14, height * .54 + bleed), palette.tone4, {
				delay: .06 + index * .04,
				enterDY: height * .1,
				edge: false
			});
		});
		addMass(ctx, rectPolygon(-bleed, height * .32, width + bleed * 2, height * .16), palette.tone3, {
			delay: .02,
			enterDX: -width * .12,
			edgeWidth: 2
		});
		addSurveyLines(ctx, 2);
	};
	const revetment = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		addGround(ctx, palette.tone1);
		const slope = [
			-bleed,
			height * .72,
			width * .62,
			height * .3,
			width + bleed,
			height * .34,
			width + bleed,
			height + bleed,
			-bleed,
			height + bleed
		];
		addMass(ctx, slope, palette.tone3, { enterDY: height * .1 });
		const ribs = Array.from({ length: 9 }, (_, index) => {
			const x = width * (.04 + index * .11);
			const top = height * (.72 - x / (width * .62) * .42);
			return {
				x1: x,
				y1: Math.max(top, height * .3) + height * .03,
				x2: x - width * .03,
				y2: height + bleed
			};
		});
		ctx.add(drawLines(ctx.pixi, ribs, palette.tone4, 2.4, .5), {
			delay: .16,
			span: .65
		});
		addSurveyLines(ctx, 3);
		if (!ctx.showDecor) return;
		addCornerTicks(ctx);
	};
	const towerCrop = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		addGround(ctx, palette.tone1);
		addMass(ctx, rectPolygon(width * .58, height * .18, width * .42 + bleed, height * .82 + bleed), palette.tone4, {
			enterDX: width * .08,
			enterDY: height * .06,
			edgeWidth: 3
		});
		addFaceRuling(ctx, rectPolygon(width * .62, height * .26, width * .12, height * .6), 257, palette.paper, .3);
		addSurveyLines(ctx, 2);
		if (!ctx.showDecor) return;
		addWireTrace(ctx, width * .2, height * .7, Math.min(width, height) * .1);
		addCornerTicks(ctx);
	};
	const lintel = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		addGround(ctx, palette.tone1);
		addMass(ctx, rectPolygon(-bleed, height * .4, width + bleed * 2, height * .2), palette.tone3, {
			enterDX: -width * .16,
			edgeWidth: 3
		});
		[.18, .66].forEach((x, index) => {
			addMass(ctx, rectPolygon(width * x, height * .6, width * .16, height * .12), palette.tone4, {
				delay: .12 + index * .04,
				enterDY: height * .06,
				edge: false
			});
		});
		addSurveyLines(ctx, 3);
		if (!ctx.showDecor) return;
		addCornerTicks(ctx);
	};
	const rubbleFan = (ctx) => {
		const { width, height, palette } = ctx;
		const hubX = -width * .12;
		const hubY = height * 1.12;
		addGround(ctx, palette.tone1);
		for (let index = 0; index < 5; index += 1) {
			const angle = -1.24 + index * .19 + visualizerHash01(ctx.seed, index, 259) * .05;
			const reach = Math.hypot(width, height) * (.5 + index * .09);
			const slab = rectPolygon(hubX + reach * .34, hubY - height * .06, reach * .5, height * .12 + index * 8);
			addMass(ctx, rotatePolygon(slab, hubX, hubY, angle), index % 2 === 0 ? palette.tone3 : palette.tone4, {
				delay: .04 + index * .05,
				enterDX: -width * .06,
				enterDY: height * .06,
				edgeWidth: 2
			});
		}
		addSurveyLines(ctx, 2);
	};
	const gnomon = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		addGround(ctx, palette.tone1);
		ctx.add(drawPolygonFill(ctx.pixi, [
			width * .42,
			height * .86,
			width + bleed,
			height * .6,
			width + bleed,
			height * .78,
			width * .46,
			height * .94
		], palette.tone2, .6, ctx.gradient), {
			delay: .1,
			span: .65,
			enterDX: width * .1
		});
		addMass(ctx, [
			width * .3,
			height + bleed,
			width * .36,
			height * .16,
			width * .42,
			height * .16,
			width * .46,
			height + bleed
		], palette.tone4, {
			delay: .04,
			enterDY: height * .08,
			edgeWidth: 2
		});
		addSurveyLines(ctx, 3);
		if (!ctx.showDecor) return;
		addCornerTicks(ctx);
		addWireTrace(ctx, width * .8, height * .82, Math.min(width, height) * .08);
	};
	const TEMPERA_TERRAIN_COMPOSITIONS = {
		"ridge-line": ridgeLine,
		"chasm": chasm,
		"overhang": overhang,
		"step-well": stepWell,
		"pier-row": pierRow,
		"revetment": revetment,
		"tower-crop": towerCrop,
		"lintel": lintel,
		"rubble-fan": rubbleFan,
		"gnomon": gnomon
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/tempera/compositions/temperaMonogatariCompositions.ts
	const addField = (ctx, tone) => {
		const { width, height, bleed } = ctx;
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, -bleed, width + bleed * 2, height + bleed * 2), tone, 1, ctx.gradient), { span: .45 });
	};
	const pickField = (ctx) => visualizerHash01(ctx.seed, 167, 173) > .5 ? ctx.palette.tone4 : ctx.palette.tone1;
	const monogatariCard = (ctx) => {
		addField(ctx, pickField(ctx));
		if (!ctx.showDecor) return;
		ctx.add(drawLines(ctx.pixi, [{
			x1: ctx.width * .12,
			y1: ctx.height * .9,
			x2: ctx.width * .88,
			y2: ctx.height * .9
		}], ctx.palette.paper, 1.2, .45), {
			delay: .22,
			span: .6,
			enterDX: ctx.width * .15
		});
	};
	const monogatariRule = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		addField(ctx, pickField(ctx));
		const y = height * (.62 + visualizerHash01(ctx.seed, 179, 181) * .08);
		ctx.add(drawPolygonFill(ctx.pixi, rectPolygon(-bleed, y, width + bleed * 2, height * .012), palette.paper, .85, ctx.gradient), {
			delay: .12,
			span: .55,
			enterDX: -width * .3
		});
		if (!ctx.showDecor) return;
		ctx.add(drawLines(ctx.pixi, [{
			x1: -bleed,
			y1: y + height * .05,
			x2: width + bleed,
			y2: y + height * .05
		}], palette.paper, 1, .35), {
			delay: .2,
			span: .6,
			enterDX: width * .2
		});
	};
	const monogatariEdge = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		const field = pickField(ctx);
		addField(ctx, field);
		const band = width * (.1 + visualizerHash01(ctx.seed, 191, 193) * .05);
		const fromLeft = visualizerHash01(ctx.seed, 197, 199) > .5;
		const spine = rectPolygon(fromLeft ? -bleed : width - band, -bleed, band + bleed, height + bleed * 2);
		ctx.add(drawPolygonFill(ctx.pixi, spine, field === palette.tone4 ? palette.tone1 : palette.tone4, .95, ctx.gradient), {
			delay: .1,
			span: .55,
			enterDX: (fromLeft ? -1 : 1) * width * .2
		});
		if (!ctx.showDecor) return;
		ctx.add(drawHatchFill(ctx.pixi, spine, buildHatchSpec(ctx.seed, 211), palette.paper, .2), {
			delay: .18,
			span: .6,
			grow: true
		});
	};
	const monogatariStack = (ctx) => {
		const { width, height, palette, bleed } = ctx;
		addField(ctx, pickField(ctx));
		const column = width * .5;
		ctx.add(drawLines(ctx.pixi, [{
			x1: (width - column) / 2,
			y1: -bleed,
			x2: (width - column) / 2,
			y2: height + bleed
		}, {
			x1: (width + column) / 2,
			y1: -bleed,
			x2: (width + column) / 2,
			y2: height + bleed
		}], palette.paper, 1.1, .4), {
			delay: .14,
			span: .6,
			enterDY: height * .12
		});
	};
	const monogatariFlash = (ctx) => {
		const { width, height, palette } = ctx;
		addField(ctx, palette.tone4);
		if (!ctx.showDecor) return;
		ctx.add(drawCrossMarks(ctx.pixi, buildCrossRow(ctx.seed, 223, width * .1, height * .18, 4, width * .05, 10), palette.paper, 2.4, .6), {
			delay: .2,
			span: .5,
			enterDX: -width * .1
		});
	};
	const TEMPERA_MONOGATARI_COMPOSITIONS = {
		"monogatari-card": monogatariCard,
		"monogatari-rule": monogatariRule,
		"monogatari-edge": monogatariEdge,
		"monogatari-stack": monogatariStack,
		"monogatari-flash": monogatariFlash
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/tempera/temperaCompositions.ts
	const COMPOSITIONS = {
		...TEMPERA_SPLIT_COMPOSITIONS,
		...TEMPERA_BAND_COMPOSITIONS,
		...TEMPERA_FRAME_COMPOSITIONS,
		...TEMPERA_POSTER_COMPOSITIONS,
		...TEMPERA_SPARSE_COMPOSITIONS,
		...TEMPERA_CINEMA_COMPOSITIONS,
		...TEMPERA_CHARM_COMPOSITIONS,
		...TEMPERA_APERTURE_COMPOSITIONS,
		...TEMPERA_SIGNAL_COMPOSITIONS,
		...TEMPERA_CORRIDOR_COMPOSITIONS,
		...TEMPERA_MONOLITH_COMPOSITIONS,
		...TEMPERA_TERRAIN_COMPOSITIONS,
		...TEMPERA_MONOGATARI_COMPOSITIONS
	};
	/** Every kind must resolve to a drawer; the registry test asserts there are no gaps. */
	const resolveTemperaComposition = (kind) => COMPOSITIONS[kind] ?? COMPOSITIONS["duo-split"];
	const addCrossingLines = (ctx) => {
		const lines = buildCrossingLines(ctx.seed, 31, ctx.width, ctx.height, ctx.decor.crossCount);
		if (lines.length === 0) return;
		ctx.add(drawLines(ctx.pixi, lines, ctx.palette.tone4, 1.3, .6), {
			delay: .14,
			span: .6,
			enterDX: ctx.width * .25
		});
	};
	const addMotif = (ctx) => {
		const { width, height, palette, decor } = ctx;
		const cornerX = visualizerHash01(ctx.seed, 61, 7) > .5 ? width * .14 : width * .86;
		const cornerY = visualizerHash01(ctx.seed, 63, 7) > .5 ? height * .18 : height * .82;
		switch (decor.motif) {
			case "diamonds":
				ctx.add(drawConcentricDiamonds(ctx.pixi, cornerX, cornerY, 34, 34, 3, palette.tone4, .8), {
					delay: .34,
					drift: true
				});
				return;
			case "hatch-twin": {
				const spec = {
					...buildHatchSpec(ctx.seed, 67),
					angle: decor.hatchAngle
				};
				[0, 1].forEach((index) => {
					const box = rectPolygon(cornerX - 34 + index * 46, cornerY - 26, 38, 38);
					ctx.add(drawHatchFill(ctx.pixi, box, index === 0 ? spec : {
						...spec,
						spacing: spec.spacing * .55
					}, palette.tone4, .75), {
						delay: .34 + index * .05,
						grow: true
					});
					ctx.add(drawPolygonOutline(ctx.pixi, box, palette.tone4, 1.2, .6), { delay: .38 + index * .05 });
				});
				return;
			}
			case "band-cross":
				ctx.add(drawCrossMarks(ctx.pixi, buildCrossRow(ctx.seed, 71, cornerX - width * .1, cornerY, 5, width * .05, 8, decor.hatchAngle * .4), palette.tone4, 2, .8), {
					delay: .34,
					span: .5
				});
				return;
			case "poster-diamond":
				ctx.add(drawPolygonFill(ctx.pixi, diamondPolygon(cornerX < width / 2 ? -width * .04 : width * 1.04, cornerY, width * .14, height * .2), palette.tone3, .85), {
					delay: .32,
					span: .55,
					enterDX: (cornerX < width / 2 ? -1 : 1) * width * .1
				});
				return;
			default: ctx.add(drawPolyline(ctx.pixi, buildScribblePath(decor.scribbleSeed, 73, cornerX, cornerY, Math.min(width, height) * .08, 3), palette.tone4, 1.5, .72), {
				delay: .34,
				span: .6
			});
		}
	};
	const drawTemperaComposition = (ctx) => {
		resolveTemperaComposition(ctx.kind)(ctx);
		if (resolveTemperaShotProfile(ctx.kind).sharedDecor === false) return;
		addCrossingLines(ctx);
		if (ctx.showDecor) addMotif(ctx);
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/tempera/temperaPalette.ts
/** Fixed paper -> ink mix positions; the screentone layer maps tone index to hatch density. */
	const TEMPERA_TONE_STOPS = [
		.12,
		.3,
		.52,
		.72
	];
	const luminanceOf = (color) => {
		const channels = parseColorChannels(color);
		if (!channels) return 128;
		return channels.r * .2126 + channels.g * .7152 + channels.b * .0722;
	};
	const toGray = (color, fallback) => {
		const channels = parseColorChannels(color);
		if (!channels) return fallback;
		const luminance = Math.round(channels.r * .2126 + channels.g * .7152 + channels.b * .0722);
		return `rgb(${luminance}, ${luminance}, ${luminance})`;
	};
	const grayLevel = (color) => parseColorChannels(color)?.r ?? 128;
	const MIN_INK_CONTRAST = 96;
	/**
	* Guarantees the ink/paper pair actually contrasts. `theme.primaryColor` carries no such
	* promise: plenty of themes pair a pale primary with a pale background, which leaves the
	* lyric unreadable and gives the difference filter two near-identical colors to choose
	* between. When that happens the ink is pushed to the opposite end of the paper and keeps
	* only a whisper of the theme hue.
	*/
	const ensureInkContrast = (paper, ink) => {
		if (Math.abs(luminanceOf(ink) - luminanceOf(paper)) >= MIN_INK_CONTRAST) return ink;
		const target = luminanceOf(paper) < 128 ? "#f4f4f2" : "#141414";
		const tinted = mixColors(target, ink, .14);
		return Math.abs(luminanceOf(tinted) - luminanceOf(paper)) >= MIN_INK_CONTRAST ? tinted : target;
	};
	const matchLuminance = (color, target) => {
		const channels = parseColorChannels(color);
		if (!channels) return target;
		const current = luminanceOf(color);
		const wanted = luminanceOf(target);
		if (current <= .5) return target;
		const gain = wanted / current;
		const scaled = {
			r: Math.round(Math.min(255, channels.r * gain)),
			g: Math.round(Math.min(255, channels.g * gain)),
			b: Math.round(Math.min(255, channels.b * gain))
		};
		if (Math.abs(luminanceOf(`rgb(${scaled.r}, ${scaled.g}, ${scaled.b})`) - wanted) > 1.5) return target;
		return `rgb(${scaled.r}, ${scaled.g}, ${scaled.b})`;
	};
	const buildToneLadder = (paper, ink, tintA, tintB) => {
		const step = (index, tint) => {
			const base = mixColors(paper, ink, TEMPERA_TONE_STOPS[index]);
			return tint ? matchLuminance(mixColors(base, tint, .3), base) : base;
		};
		return {
			tone1: step(0, tintA),
			tone2: step(1, tintB),
			tone3: step(2, tintA),
			tone4: step(3)
		};
	};
	/**
	* Builds the four-colour gradient ramp. Cover-art colours carry the hue, but each one is
	* pulled onto the paper -> ink brightness ladder first: an unordered ramp would break both
	* the composition's tonal structure and the text inversion that reads it.
	*/
	const buildGradientRamp = (paper, ink, sources) => {
		const ranked = [...sources.map((color) => color.trim()).filter(Boolean)].sort((a, b) => luminanceOf(a) - luminanceOf(b));
		const ordered = luminanceOf(ink) < luminanceOf(paper) ? ranked.reverse() : ranked;
		return TEMPERA_TONE_STOPS.map((stop, index) => {
			const rung = mixColors(paper, ink, stop);
			const hue = ordered[index % Math.max(ordered.length, 1)];
			return hue ? matchLuminance(mixColors(rung, hue, .78), rung) : rung;
		});
	};
	/**
	* Keeps a cover colour recognisable while guaranteeing it reads against the paper. Rather than
	* pulling it onto the ink ladder (which is what greys the background ramp out), it is only
	* pushed away from the paper's luminance until it clears the floor.
	*/
	const enforceReadable = (paper, color) => {
		const paperLuminance = luminanceOf(paper);
		if (Math.abs(luminanceOf(color) - paperLuminance) >= 88) return color;
		const away = paperLuminance < 128 ? "#ffffff" : "#101010";
		for (let step = 1; step <= 5; step += 1) {
			const pushed = mixColors(color, away, step * .16);
			if (Math.abs(luminanceOf(pushed) - paperLuminance) >= 88) return pushed;
		}
		return mixColors(color, away, .8);
	};
	/**
	* How much of the theme is mixed into each extracted cover colour. The cover stays dominant -
	* it is what the mode is sampling - but a purely cover-derived ramp drops the theme entirely,
	* which makes the mode ignore the user's palette exactly when it is most visible.
	*/
	const THEME_HUE_MIX = .32;
	const blendCoverWithTheme = (cover, themeHues) => cover.slice(0, 4).map((color, index) => mixColors(color, themeHues[index % themeHues.length], THEME_HUE_MIX));
	const buildTextGradient = (paper, sources) => {
		const usable = sources.map((color) => color.trim()).filter(Boolean);
		if (usable.length === 0) return null;
		return Array.from({ length: 4 }, (_, index) => enforceReadable(paper, usable[index % usable.length]));
	};
	const resolveTemperaPalette = (theme, tuning, coverColors = []) => {
		if (tuning.colorMode === "mono") {
			const paper = toGray(theme.backgroundColor, "#111111");
			let ink = toGray(theme.primaryColor, "#f5f5f5");
			if (Math.abs(grayLevel(ink) - grayLevel(paper)) < 96) ink = grayLevel(paper) < 128 ? "#f2f2f2" : "#141414";
			return {
				paper,
				ink,
				blockA: mixColors(paper, ink, .08),
				blockB: mixColors(paper, ink, .18),
				blockC: mixColors(paper, ink, .34),
				accent: mixColors(paper, ink, .85),
				line: colorWithAlpha(mixColors(paper, ink, .55), .55),
				shadow: colorWithAlpha(mixColors(paper, ink, .75), .35),
				...buildToneLadder(paper, ink),
				gradient: null,
				textGradient: null
			};
		}
		const paper = theme.backgroundColor;
		const ink = ensureInkContrast(paper, theme.primaryColor);
		if (tuning.colorMode === "gradient") {
			const themeHues = [
				theme.accentColor,
				theme.secondaryColor,
				theme.primaryColor,
				ink
			];
			const hues = coverColors.length >= 2 ? blendCoverWithTheme(coverColors, themeHues) : themeHues;
			const ramp = buildGradientRamp(paper, ink, hues);
			return {
				paper,
				ink,
				blockA: ramp[0],
				blockB: ramp[1],
				blockC: ramp[2],
				accent: theme.accentColor,
				line: colorWithAlpha(mixColors(paper, ink, .6), .5),
				shadow: colorWithAlpha(mixColors(paper, ink, .8), .32),
				tone1: ramp[0],
				tone2: ramp[1],
				tone3: ramp[2],
				tone4: ramp[3],
				gradient: ramp,
				textGradient: buildTextGradient(paper, hues)
			};
		}
		return {
			paper,
			ink,
			blockA: mixColors(paper, theme.accentColor, .55),
			blockB: mixColors(paper, theme.secondaryColor, .6),
			blockC: mixColors(paper, ink, .78),
			accent: theme.accentColor,
			line: colorWithAlpha(mixColors(paper, ink, .6), .5),
			shadow: colorWithAlpha(mixColors(paper, ink, .8), .32),
			...buildToneLadder(paper, ink, theme.accentColor, theme.secondaryColor),
			gradient: null,
			textGradient: null
		};
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/tempera/temperaEnterStyles.ts
	const TEMPERA_ENTER_STYLES = [
		"slide",
		"from-left",
		"from-right",
		"from-above",
		"from-below",
		"swing",
		"stamp"
	];

//#endregion
//#region ../../web/folia-major/src/components/visualizer/tempera/temperaMeasure.ts
/**
	* Measurement is shared across every layout call, not scoped to one. A cache key names the
	* whole spec (`weight size family|text`), so nothing about a scene, a shot or a song can make
	* two entries with the same key disagree - and the same graphemes recur constantly: the fit
	* loop re-measures a shot up to four times, a paragraph has several shots, and consecutive
	* songs share most of their character set. A per-call cache threw all of that away and made a
	* song change re-measure everything from scratch on the frame it landed.
	*/
	const MEASURE_CACHE_LIMIT$1 = 2e4;
	const measureCache$1 = /* @__PURE__ */ new Map();
	const readMeasureCache = (key) => measureCache$1.get(key);
	const writeMeasureCache = (key, width) => {
		if (measureCache$1.size >= MEASURE_CACHE_LIMIT$1) {
			const oldest = measureCache$1.keys().next();
			if (!oldest.done) measureCache$1.delete(oldest.value);
		}
		measureCache$1.set(key, width);
	};
	const createTemperaMeasureContext = (fontFamily, fontWeight) => ({
		cache: measureCache$1,
		fontFamily,
		fontWeight
	});
	const measureText$1 = (ctx, text, fontSize) => {
		const fontSpec = `${ctx.fontWeight} ${fontSize}px ${ctx.fontFamily}`;
		const key = `${fontSpec}|${text}`;
		const cached = readMeasureCache(key);
		if (cached !== void 0) return cached;
		let measured;
		try {
			measured = layoutWithLines(prepareWithSegments(text, fontSpec), 99999, fontSize * 1.2).lines[0]?.width ?? text.length * fontSize * .6;
		} catch {
			measured = text.length * fontSize * .6;
		}
		const width = Math.max(fontSize * .08, measured);
		writeMeasureCache(key, width);
		return width;
	};
	const measureTemperaGrapheme = (ctx, char, fontSize) => char.trim().length === 0 ? fontSize * .3 : measureText$1(ctx, char, fontSize);
	const buildTemperaWordUnit = (ctx, segment, lineIndex, segmentIndex, fontSize, scale) => {
		const glyphs = segment.graphemes.filter((grapheme) => grapheme.char.length > 0);
		if (glyphs.length === 0) return null;
		const scaledSize = fontSize * scale;
		const raw = glyphs.map((grapheme) => measureTemperaGrapheme(ctx, grapheme.char, scaledSize));
		const rawTotal = raw.reduce((sum, value) => sum + value, 0);
		const shaped = measureText$1(ctx, segment.text.replace(/\s+$/u, ""), scaledSize);
		const correction = rawTotal > 0 ? shaped / rawTotal : 1;
		let offset = 0;
		const placed = glyphs.map((grapheme, index) => {
			const width = raw[index] * correction;
			const glyph = {
				char: grapheme.char,
				startTime: grapheme.startTime,
				endTime: grapheme.endTime,
				offset,
				width
			};
			offset += width;
			return glyph;
		});
		return {
			lineIndex,
			segmentIndex,
			text: segment.text,
			startOffset: segment.startOffset,
			endOffset: segment.endOffset,
			leadingGap: 0,
			scale,
			width: offset,
			glyphs: placed,
			startTime: placed[0].startTime,
			endTime: placed[placed.length - 1].endTime
		};
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/tempera/temperaLayout.ts
	const resolveRegion = (shotKind, width, height) => {
		const { region } = resolveTemperaShotProfile(shotKind);
		return {
			centerX: region.cx * width,
			centerY: region.cy * height,
			width: region.w * width,
			height: region.h * height,
			align: region.align,
			rotation: region.rotation,
			fontScale: region.fontScale
		};
	};
	const resolveEnterVector = (shotKind, fontSize) => {
		const { enter } = resolveTemperaShotProfile(shotKind);
		return {
			x: enter.x * fontSize,
			y: enter.y * fontSize
		};
	};
	const rotateAbout = (x, y, cx, cy, angle) => {
		if (angle === 0) return {
			x,
			y
		};
		const cos = Math.cos(angle);
		const sin = Math.sin(angle);
		const dx = x - cx;
		const dy = y - cy;
		return {
			x: cx + dx * cos - dy * sin,
			y: cy + dx * sin + dy * cos
		};
	};
	const resolveWordScales = (segments, seed, lineIndex) => {
		const lengths = segments.map((segment) => segment.isWordLike ? segment.graphemes.length : 0);
		const maxLength = Math.max(0, ...lengths);
		const heroCandidates = lengths.flatMap((length, index) => length > 0 && length >= maxLength - 1 ? [index] : []);
		const heroIndex = heroCandidates.length > 0 ? heroCandidates[Math.floor(visualizerHash01(seed, lineIndex, 101) * heroCandidates.length) % heroCandidates.length] : -1;
		return segments.map((_, index) => index === heroIndex ? 1.34 + visualizerHash01(seed, lineIndex * 31 + index, 103) * .26 : .7 + visualizerHash01(seed, lineIndex * 31 + index, 107) * .16);
	};
	const packRows = (words, maxWidth, seed) => {
		const rows = [];
		let current = [];
		let currentWidth = 0;
		const budgetFor = (rowIndex) => maxWidth * (.72 + visualizerHash01(seed, rowIndex, 109) * .28);
		const flush = () => {
			if (current.length === 0) return;
			rows.push({
				words: current,
				width: currentWidth,
				height: 0
			});
			current = [];
			currentWidth = 0;
		};
		words.forEach((word) => {
			const newLine = current.length > 0 && word.lineIndex !== current[0].lineIndex;
			const advance = current.length === 0 ? word.width : word.width + word.leadingGap;
			if (newLine || current.length > 0 && currentWidth + advance > budgetFor(rows.length)) flush();
			currentWidth += current.length === 0 ? word.width : advance;
			current.push(word);
		});
		flush();
		return rows;
	};
	/**
	* Word segmentation exists to pick font sizes, not to space the text out. The gap before a
	* word is a real space only when the source actually had whitespace there; a plain
	* segmentation boundary (every CJK word break) gets nothing but an optical hair.
	*/
	const buildWordUnits = (ctx, lines, fontSize, seed) => lines.flatMap((segments, lineIndex) => {
		const scales = resolveWordScales(segments, seed, lineIndex);
		let previousEnd = null;
		return segments.flatMap((segment, segmentIndex) => {
			const unit = buildTemperaWordUnit(ctx, segment, lineIndex, segmentIndex, fontSize, scales[segmentIndex]);
			if (!unit) return [];
			const spaced = previousEnd !== null && segment.startOffset > previousEnd;
			unit.leadingGap = previousEnd === null ? 0 : fontSize * (spaced ? .26 : .035);
			previousEnd = segment.endOffset;
			return [unit];
		});
	});
	/** Shortest entrance any glyph gets, whatever else the shot's pace says. */
	const MIN_SETTLE_SECONDS = .34;
	/**
	* Default for how much of the way to the shot's lyric end an entrance stretches, past the
	* floor. Exposed as a tuning (`glyphSettleStretch`); this is the single dial between the two
	* behaviours that were tried on their own:
	*
	* - 0 gives every glyph the same short window regardless of where it sits in the shot. Dense,
	*   quickly cut songs read percussively that way - each glyph snaps in ~0.09s and then holds -
	*   but a slow phrase lands everything in the first second and then nothing moves.
	* - 1 lands the whole shot exactly on its lyric end. That keeps a slow phrase alive the whole
	*   way through, but leaves a fast shot with no moment at rest at all: the mean dwell before
	*   the cut goes to zero, every glyph is still travelling, and the type reads as mush.
	*
	* Half keeps most of the shot settled before it cuts while still giving a long phrase an
	* entrance that runs deep into it.
	*/
	const DEFAULT_SETTLE_STRETCH = .5;
	/**
	* Paces every entrance against the end of the lyric *this shot* carries, then sets the
	* post-sung release.
	*
	* A glyph starts on its own lyric time and its window is the floor plus a share of whatever
	* distance is left to that end, so the stagger shortens smoothly from the first glyph to the
	* last and the shot arrives as one sweep rather than as a row of independent pops. Because the
	* curve is heavily front-loaded, a long window is a decisive opening followed by a slow creep,
	* not a slow arrival.
	*
	* The target is the shot's own lyric end. By default a shot shows a half-phrase and one source
	* line commonly runs across several shots; whole-line mode instead makes that shot end equal
	* the line's lyric end. In either case `lines` is already the exact slice set this shot carries,
	* and slices shown together are paced together. Only a glyph that starts within the floor of
	* the end lands after it.
	*
	* The alpha and echo ramps do not follow this window (see temperaMotion): a glyph that stayed
	* half-transparent for the length of a shot would not be readable.
	*
	* The release is separate: a glyph that stops moving the moment it has been sung leaves the
	* whole line frozen for the rest of a long shot, so the block slowly widens its tracking from
	* its centre outward and each glyph's lever arm is simply its offset from that centre. That
	* ramp lasts as long as the glyph's own line and no longer.
	*/
	const applyGlyphTiming = (placements, settleStretch) => {
		if (placements.length === 0) return placements;
		const stretch = Number.isFinite(settleStretch) ? Math.min(1, Math.max(0, settleStretch)) : DEFAULT_SETTLE_STRETCH;
		const lineSpans = /* @__PURE__ */ new Map();
		placements.forEach((placement) => {
			const current = lineSpans.get(placement.lineIndex) ?? 0;
			lineSpans.set(placement.lineIndex, Math.max(current, placement.endTime));
		});
		const lineStarts = /* @__PURE__ */ new Map();
		placements.forEach((placement) => {
			const current = lineStarts.get(placement.lineIndex) ?? Number.POSITIVE_INFINITY;
			lineStarts.set(placement.lineIndex, Math.min(current, placement.startTime));
		});
		const shotLyricEnd = placements.reduce((latest, p) => Math.max(latest, p.endTime), 0);
		placements.forEach((placement) => {
			const reach = Math.max(0, shotLyricEnd - placement.startTime - MIN_SETTLE_SECONDS);
			placement.settleTime = placement.startTime + MIN_SETTLE_SECONDS + reach * stretch;
		});
		const centerX = placements.reduce((sum, placement) => sum + placement.x, 0) / placements.length;
		const centerY = placements.reduce((sum, placement) => sum + placement.y, 0) / placements.length;
		placements.forEach((placement) => {
			const span = Math.max(.5, (lineSpans.get(placement.lineIndex) ?? placement.endTime) - (lineStarts.get(placement.lineIndex) ?? placement.startTime));
			placement.releaseTime = Math.max(placement.endTime, placement.settleTime) + span;
			placement.trackingX = placement.x - centerX;
			placement.trackingY = placement.y - centerY;
		});
		return placements;
	};
	const resolveTemperaLayout = ({ lines, shotKind, width, height, baseFontSize, fontFamily, fontWeight, seed, segmentColors, settleStretch = DEFAULT_SETTLE_STRETCH }) => {
		const region = resolveRegion(shotKind, width, height);
		const ctx = createTemperaMeasureContext(fontFamily, fontWeight);
		let fontSize = Math.max(14, baseFontSize * region.fontScale);
		let rows = [];
		let blockHeight = 0;
		for (let attempt = 0; attempt < 4; attempt += 1) {
			rows = packRows(buildWordUnits(ctx, lines, fontSize, seed), region.width, seed);
			rows.forEach((row, rowIndex) => {
				const tallest = Math.max(...row.words.map((word) => word.scale));
				row.height = fontSize * tallest * (1.02 + visualizerHash01(seed, rowIndex, 113) * .1);
			});
			blockHeight = rows.reduce((sum, row) => sum + row.height, 0);
			const maxRowWidth = Math.max(1, ...rows.map((row) => row.width));
			const fit = Math.min(1, blockHeight > region.height ? region.height / blockHeight : 1, maxRowWidth > region.width ? region.width / maxRowWidth : 1);
			if (fit >= .999) break;
			fontSize *= fit;
		}
		const base = resolveEnterVector(shotKind, fontSize);
		const baseAngle = Math.atan2(base.y, base.x);
		const baseMagnitude = Math.hypot(base.x, base.y);
		const placements = [];
		let cursorY = region.centerY - blockHeight / 2;
		rows.forEach((row, rowIndex) => {
			const rowCenterY = cursorY + row.height / 2;
			cursorY += row.height;
			const drift = (visualizerHash01(seed, rowIndex, 127) - .5) * region.width * .06;
			const rowRotation = (visualizerHash01(seed, rowIndex, 131) - .5) * .06;
			const rowLeft = region.align === "center" ? region.centerX - row.width / 2 + drift : region.align === "left" ? region.centerX - region.width / 2 + Math.abs(drift) * .6 : region.centerX + region.width / 2 - row.width - Math.abs(drift) * .6;
			const rowCenterX = rowLeft + row.width / 2;
			let cursorX = rowLeft;
			row.words.forEach((word, wordIndex) => {
				const salt = rowIndex * 37 + wordIndex;
				const enterStyle = TEMPERA_ENTER_STYLES[Math.floor(visualizerHash01(seed, salt, 193) * TEMPERA_ENTER_STYLES.length) % TEMPERA_ENTER_STYLES.length];
				const wordRotation = (visualizerHash01(seed, salt, 137) - .5) * .07;
				const wordShiftY = (visualizerHash01(seed, salt, 139) - .5) * fontSize * .09;
				const wordLeft = cursorX + (wordIndex === 0 ? 0 : word.leadingGap);
				cursorX = wordLeft + word.width;
				const wordCenterX = wordLeft + word.width / 2;
				const wordCenterY = rowCenterY + wordShiftY;
				const glyphSize = fontSize * word.scale;
				word.glyphs.forEach((glyph, glyphIndex) => {
					if (glyph.char.trim().length === 0) return;
					const localX = wordLeft + glyph.offset + glyph.width / 2;
					const rotatedWord = rotateAbout(localX, wordCenterY, wordCenterX, wordCenterY, wordRotation);
					const rotatedRow = rotateAbout(rotatedWord.x, rotatedWord.y, rowCenterX, rowCenterY, rowRotation);
					const finalPoint = rotateAbout(rotatedRow.x, rotatedRow.y, region.centerX, region.centerY, region.rotation);
					const glyphSalt = salt * 53 + glyphIndex;
					const spread = (visualizerHash01(seed, glyphSalt, 149) - .5) * 1.7;
					const magnitude = baseMagnitude * (.55 + visualizerHash01(seed, glyphSalt, 151) * 1.05);
					const angle = baseAngle + spread;
					placements.push({
						char: glyph.char,
						lineIndex: word.lineIndex,
						segmentIndex: word.segmentIndex,
						x: finalPoint.x,
						y: finalPoint.y,
						rotation: region.rotation + rowRotation + wordRotation,
						startTime: glyph.startTime,
						endTime: glyph.endTime,
						settleTime: glyph.endTime,
						fontSize: glyphSize,
						color: segmentColors?.[word.lineIndex]?.[word.segmentIndex] ?? null,
						enterX: Math.cos(angle) * magnitude,
						enterY: Math.sin(angle) * magnitude,
						enterRotation: (visualizerHash01(seed, glyphSalt, 157) - .5) * .7,
						enterScale: .6 + visualizerHash01(seed, glyphSalt, 163) * .3,
						enterStyle,
						releaseTime: 0,
						trackingX: 0,
						trackingY: 0
					});
				});
			});
		});
		return applyGlyphTiming(placements, settleStretch);
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/sonnet/types.ts
	const SONNET_TRANSITION_KINDS = [
		"fast-blur",
		"mono-glitch",
		"camera-pull"
	];

//#endregion
//#region \0semantic
	const buildSonnetSemanticSegments = (line) => line._segments ?? [];

//#endregion
//#region ../../web/folia-major/src/components/visualizer/sonnet/sonnetProgram.ts
	const SONNET_SHOT_KINDS = [
		"editorial-column",
		"type-impact",
		"fragment-collage",
		"tracking-ribbon",
		"mask-reveal",
		"poster-blocks",
		"quiet-tableau"
	];
	const SONNET_DEBUG_SHOT_KIND = null;
	const resolveSonnetDebugShotKind = () => null;
	const clamp$3 = (value, min, max) => Math.min(max, Math.max(min, value));
	const median = (values) => {
		if (values.length === 0) return .5;
		const sorted = [...values].sort((a, b) => a - b);
		const middle = Math.floor(sorted.length / 2);
		return sorted.length % 2 === 0 ? ((sorted[middle - 1] ?? sorted[middle]) + sorted[middle]) / 2 : sorted[middle];
	};
	const resolveSonnetParagraphGapThreshold = (lines) => {
		const gaps = lines.slice(1).map((line, index) => line.startTime - Math.min(getLineRenderEndTime(lines[index]), line.startTime)).filter((gap) => gap > 0);
		return clamp$3(median(gaps) * 2.5, 1.25, 3.5);
	};
	const metadataChanged = (previous, next) => previous.blockIndex !== void 0 && next.blockIndex !== void 0 && previous.blockIndex !== next.blockIndex || previous.songPart !== void 0 && next.songPart !== void 0 && previous.songPart !== next.songPart;
	const splitOversizedDraft = (draft) => {
		const output = [];
		let remaining = draft.lines;
		let boundary = draft.boundary;
		let loopGuard = 0;
		while (remaining.length > 6 || remaining.length > 1 && remaining.at(-1).renderEndTime - remaining[0].line.startTime > 18) {
			if (loopGuard++ > 1e3) {
				console.error("splitOversizedDraft: Infinite loop detected, breaking");
				break;
			}
			const rawSplitIndex = remaining.slice(2, -1).map((line, offset) => ({
				splitIndex: offset + 2,
				gap: line.line.startTime - remaining[offset + 1].renderEndTime
			})).filter((c) => !Number.isNaN(c.gap)).sort((a, b) => b.gap - a.gap)[0]?.splitIndex ?? Math.min(4, remaining.length - 1);
			const splitIndex = Math.max(1, rawSplitIndex);
			output.push({
				lines: remaining.slice(0, splitIndex),
				boundary
			});
			remaining = remaining.slice(splitIndex);
			boundary = output.at(-1).lines.length >= 6 ? "line-cap" : "duration-cap";
		}
		output.push({
			lines: remaining,
			boundary
		});
		return output;
	};
	const classifyParagraph = (lines, index, total) => {
		if (lines.some((item) => item.line.isChorus || /chorus|副歌/i.test(item.line.songPart ?? ""))) return "chorus";
		if (lines.some((item) => /bridge|break|間奏|ブリッジ/i.test(item.line.songPart ?? ""))) return "break";
		if (index === total - 1) return "outro";
		const duration = lines.at(-1).renderEndTime - lines[0].line.startTime;
		const segmentCount = lines.reduce((sum, line) => sum + line.segments.filter((segment) => segment.isWordLike).length, 0);
		const punctuationCount = lines.reduce((sum, line) => sum + (line.line.fullText.match(/[!?！？…]/g)?.length ?? 0), 0);
		if (duration <= 3.5 || segmentCount <= 3) return "breath";
		if (punctuationCount >= 2 || segmentCount / Math.max(duration, 1) > 2.5) return "lift";
		return "verse";
	};
	const chooseWithoutRepeat = (choices, seed, previous) => {
		const start = hashVisualizerSeed(seed) % choices.length;
		for (let offset = 0; offset < choices.length; offset += 1) {
			const candidate = choices[(start + offset) % choices.length];
			if (candidate !== previous) return candidate;
		}
		return choices[start];
	};
	const buildCues = (lines) => {
		const segments = lines.flatMap((line) => line.segments).filter((segment) => segment.text.length > 0);
		return segments.map((segment, index) => ({
			at: segment.startTime,
			duration: Math.max(.08, segment.endTime - segment.startTime),
			kind: index === segments.length - 1 ? "accent" : "enter",
			segmentStart: index,
			segmentEnd: index + 1
		}));
	};
	const groupShotLines = (lines) => {
		const groups = [];
		let currentGroup = [];
		let groupStartTime = 0;
		for (let index = 0; index < lines.length; index += 1) {
			const line = lines[index];
			if (currentGroup.length === 0) {
				currentGroup.push(line);
				groupStartTime = line.line.startTime;
			} else {
				const durationSoFar = line.renderEndTime - groupStartTime;
				if (currentGroup.length < 4 && durationSoFar <= 6) currentGroup.push(line);
				else {
					groups.push(currentGroup);
					currentGroup = [line];
					groupStartTime = line.line.startTime;
				}
			}
		}
		if (currentGroup.length > 0) groups.push(currentGroup);
		return groups;
	};
	const buildShots = (lines, kind, paragraphIndex, seed, previousKind) => {
		let lastKind = previousKind;
		return groupShotLines(lines).map((group, shotIndex) => {
			const signature = group.map((item) => item.line.fullText).join("|");
			const debugShotKind = resolveSonnetDebugShotKind();
			let shotKind = debugShotKind ?? chooseWithoutRepeat(SONNET_SHOT_KINDS, `${seed}:${paragraphIndex}:${shotIndex}:${signature}`, lastKind);
			const wordCount = group.reduce((sum, item) => sum + item.segments.filter((s) => s.isWordLike).length, 0);
			if (debugShotKind === null) {
				if (kind === "breath" && shotIndex === 0 && wordCount <= 2) shotKind = "quiet-tableau";
				if (kind === "chorus" && shotKind === "quiet-tableau") shotKind = "type-impact";
			}
			lastKind = shotKind;
			const random = hashVisualizerSeed(`${seed}:${paragraphIndex}:${shotIndex}:camera`);
			const zoomRandom = (random >>> 16 & 255) / 255;
			const zoomBase = shotKind === "poster-blocks" ? 1.02 : shotKind === "quiet-tableau" ? 1.12 : 1.22;
			const zoomSpan = shotKind === "poster-blocks" ? .16 : shotKind === "quiet-tableau" ? .2 : .26;
			return {
				id: `p${paragraphIndex}-s${shotIndex}`,
				kind: shotKind,
				startTime: group[0].line.startTime,
				endTime: group.at(-1).renderEndTime,
				lineIndices: group.map((item) => item.sourceIndex),
				cues: buildCues(group),
				camera: {
					x: ((random & 255) / 255 - .5) * .18,
					y: ((random >>> 8 & 255) / 255 - .5) * .14,
					zoom: zoomBase + zoomRandom * zoomSpan,
					rotation: ((random >>> 24 & 255) / 255 - .5) * .08
				}
			};
		});
	};
	const compileSonnetProgram = (lines, seed = "sonnet") => {
		const compiled = lines.map((line, sourceIndex) => ({
			sourceIndex,
			line,
			renderEndTime: Math.max(line.startTime, Math.min(getLineRenderEndTime(line), lines[sourceIndex + 1]?.startTime ?? Number.POSITIVE_INFINITY)),
			segments: buildSonnetSemanticSegments(line)
		}));
		const paragraphGapThreshold = resolveSonnetParagraphGapThreshold(lines);
		const drafts = [];
		let current = {
			lines: [],
			boundary: "song-start"
		};
		compiled.forEach((line, index) => {
			const previous = compiled[index - 1];
			const gap = previous ? line.line.startTime - previous.renderEndTime : 0;
			const boundary = previous && metadataChanged(previous.line, line.line) ? "metadata" : previous && gap >= paragraphGapThreshold ? "time-gap" : null;
			if (boundary && current.lines.length > 0) {
				drafts.push(...splitOversizedDraft(current));
				current = {
					lines: [],
					boundary
				};
			}
			current.lines.push(line);
		});
		if (current.lines.length > 0) drafts.push(...splitOversizedDraft(current));
		const resolvedSeed = String(seed);
		let previousShot = null;
		let previousTransition = null;
		return {
			version: 1,
			seed: resolvedSeed,
			paragraphGapThreshold,
			paragraphs: drafts.map((draft, index) => {
				const kind = classifyParagraph(draft.lines, index, drafts.length);
				const shots = buildShots(draft.lines, kind, index, resolvedSeed, previousShot);
				previousShot = shots.at(-1)?.kind ?? previousShot;
				const next = drafts[index + 1];
				const endTime = draft.lines.at(-1).renderEndTime;
				const gap = next ? next.lines[0].line.startTime - endTime : 0;
				const availableTransitions = [...SONNET_TRANSITION_KINDS];
				const transitionKind = next ? chooseWithoutRepeat(availableTransitions, `${resolvedSeed}:${index}:transition`, previousTransition) : null;
				if (transitionKind) previousTransition = transitionKind;
				const transitionDuration = next ? Math.min(.3, Math.max(.16, gap > 0 ? gap * .5 : .2)) : 0;
				const transitionEndTime = next?.lines[0].line.startTime ?? endTime;
				return {
					id: `sonnet-p${index}`,
					kind,
					boundary: draft.boundary,
					startTime: draft.lines[0].line.startTime,
					endTime,
					lines: draft.lines,
					shots,
					transitionOut: transitionKind ? {
						kind: transitionKind,
						startTime: Math.max(draft.lines[0].line.startTime, transitionEndTime - transitionDuration),
						endTime: transitionEndTime
					} : null
				};
			})
		};
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/sonnet/sonnetTypographyRoles.ts
	const isSonnetEmphasisRole = (role) => role === "hero" || role === "semi-hero";
	/** Uses Sonnet's designed role weights in auto mode, or the user's global manual override. */
	const resolveSonnetRoleFontWeight = (configuredFontWeight, role) => {
		const manualWeight = normalizeFontWeight(configuredFontWeight);
		if (manualWeight !== null) return manualWeight;
		if (isSonnetEmphasisRole(role)) return 900;
		return role === "decoration" ? 300 : 700;
	};
	const getSonnetVisibleSegmentLength = (segment) => segment.graphemes.filter((item) => item.char.trim().length > 0).length;
	const scoreSonnetHeroSegment = (segment) => {
		return Math.min(getSonnetVisibleSegmentLength(segment), 8) * 14 + Math.min(2.5, Math.max(0, segment.endTime - segment.startTime)) * 18;
	};
	const findSonnetHeroSegmentIndex = (segments) => {
		let bestIndex = segments.findIndex((segment) => segment.isWordLike);
		let bestScore = -Infinity;
		segments.forEach((segment, index) => {
			if (!segment.isWordLike || getSonnetVisibleSegmentLength(segment) === 0) return;
			const score = scoreSonnetHeroSegment(segment);
			if (score > bestScore) {
				bestScore = score;
				bestIndex = index;
			}
		});
		return Math.max(0, bestIndex);
	};
	const SEMI_HERO_MIN_GAP = 2;
	const SEMI_HERO_MIN_VISIBLE_LENGTH = 2;
	const SEMI_HERO_MIN_LINE_WORDS = 4;
	const SEMI_HERO_SCORE_RATIO = .35;
	const SEMI_HERO_MULTI_WORD_COUNT = 9;
	const findSonnetSemiHeroSegmentIndices = (segments, heroIndex) => {
		const hero = segments[heroIndex];
		if (!hero) return [];
		const wordLikeCount = segments.filter((segment) => segment.isWordLike && getSonnetVisibleSegmentLength(segment) > 0).length;
		if (wordLikeCount < SEMI_HERO_MIN_LINE_WORDS) return [];
		const threshold = scoreSonnetHeroSegment(hero) * SEMI_HERO_SCORE_RATIO;
		const candidates = segments.map((segment, index) => ({
			segment,
			index
		})).filter(({ segment, index }) => index !== heroIndex && segment.isWordLike && getSonnetVisibleSegmentLength(segment) >= SEMI_HERO_MIN_VISIBLE_LENGTH && Math.abs(index - heroIndex) >= SEMI_HERO_MIN_GAP && scoreSonnetHeroSegment(segment) >= threshold);
		if (candidates.length === 0) return [];
		const bestOf = (list) => list.reduce((best, item) => !best || scoreSonnetHeroSegment(item.segment) > scoreSonnetHeroSegment(best.segment) ? item : best, null);
		const heroLeansEarly = heroIndex <= (segments.length - 1) / 2;
		const primarySide = candidates.filter(({ index }) => heroLeansEarly ? index > heroIndex : index < heroIndex);
		const secondarySide = candidates.filter(({ index }) => heroLeansEarly ? index < heroIndex : index > heroIndex);
		const picks = [];
		const primary = bestOf(primarySide) ?? bestOf(secondarySide);
		if (primary) picks.push(primary.index);
		if (wordLikeCount >= SEMI_HERO_MULTI_WORD_COUNT && primary) {
			const secondary = bestOf(secondarySide.filter(({ index }) => Math.abs(index - primary.index) >= SEMI_HERO_MIN_GAP));
			if (secondary) picks.push(secondary.index);
		}
		return picks.sort((first, second) => first - second);
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/sonnet/sonnetPosterBlocksLayout.ts
	const clamp$2 = (value, min, max) => Math.min(max, Math.max(min, value));
	const partitionFlowItems = (boxes) => {
		const items = [];
		let group = [];
		boxes.forEach((box) => {
			if (box.isHero || box.isSemiHero) {
				if (group.length > 0) items.push({
					kind: "group",
					group
				});
				group = [];
				items.push({
					kind: "zone",
					zone: box
				});
			} else group.push(box);
		});
		if (group.length > 0) items.push({
			kind: "group",
			group
		});
		return items;
	};
	const flowToScreen = (space, rect, canvas) => {
		if (space.orientation === "horizontal") return {
			x: canvas.x + rect.u,
			y: canvas.y + rect.v,
			width: rect.uSize,
			height: rect.vSize
		};
		return {
			x: canvas.x + canvas.width - rect.v - rect.vSize,
			y: canvas.y + rect.u,
			width: rect.vSize,
			height: rect.uSize
		};
	};
	const attemptFlowLayout = (boxes, space, globalScale, chipGap, lineGap, seed) => {
		const items = partitionFlowItems(boxes);
		const placements = [];
		const floats = [];
		let vCursor = 0;
		let ownBandOnEndSide = (seed >> 1 & 1) === 1;
		const measure = (box) => {
			const useVertical = space.orientation === "vertical" && typeof box.verticalMeasuredWidth === "number" && typeof box.verticalMeasuredHeight === "number" && typeof box.verticalFontScale === "number";
			return {
				useVertical,
				baseScale: useVertical ? box.verticalFontScale : box.fontScale,
				width: (useVertical ? box.verticalMeasuredWidth : box.measuredWidth) * globalScale,
				height: (useVertical ? box.verticalMeasuredHeight : box.measuredHeight) * globalScale
			};
		};
		const toFlowSize = (width, height) => space.orientation === "horizontal" ? {
			uSize: width,
			vSize: height
		} : {
			uSize: height,
			vSize: width
		};
		const pruneFloats = () => {
			for (let index = floats.length - 1; index >= 0; index--) if (floats[index].vBottom <= vCursor) floats.splice(index, 1);
		};
		items.forEach((item, itemIndex) => {
			pruneFloats();
			if (item.kind === "group") {
				const reservedU = floats.reduce((sum, entry) => sum + entry.extent, 0);
				const capacity = Math.max(chipGap * 2, space.u - reservedU);
				const uStart = reservedU;
				const chips = item.group.map((box) => {
					const dims = measure(box);
					const flow = toFlowSize(dims.width, dims.height);
					return {
						box,
						dims,
						uSize: flow.uSize,
						vSize: flow.vSize,
						shrink: 1
					};
				});
				let line = [];
				let lineUsedU = 0;
				const flushLine = () => {
					if (line.length === 0) return;
					const lineV = Math.max(...line.map((chip) => chip.vSize * chip.shrink));
					const leftover = capacity - lineUsedU;
					const spread = line.length > 1 && leftover > 0 ? Math.min(leftover / (line.length - 1), chipGap * 2.5) : 0;
					let uCursor = uStart;
					line.forEach((chip) => {
						const finalScale = chip.dims.baseScale * globalScale * chip.shrink;
						placements.push({
							box: chip.box,
							rect: {
								u: uCursor,
								v: vCursor,
								uSize: chip.uSize * chip.shrink,
								vSize: chip.vSize * chip.shrink
							},
							scale: finalScale,
							vertical: chip.dims.useVertical
						});
						uCursor += chip.uSize * chip.shrink + chipGap + spread;
					});
					vCursor += lineV + lineGap;
					pruneFloats();
					line = [];
					lineUsedU = 0;
				};
				chips.forEach((chip) => {
					if (lineUsedU + (line.length > 0 ? chipGap : 0) + chip.uSize > capacity && line.length > 0) flushLine();
					if (chip.uSize > capacity) {
						chip.shrink = Math.max(.5, capacity / chip.uSize);
						lineUsedU = 0;
						line.push(chip);
						flushLine();
						return;
					}
					lineUsedU += (line.length > 0 ? chipGap : 0) + chip.uSize;
					line.push(chip);
				});
				flushLine();
				return;
			}
			const zone = item.zone;
			vCursor = Math.max(vCursor, ...floats.map((entry) => entry.vBottom), 0);
			floats.length = 0;
			const dims = measure(zone);
			const flow = toFlowSize(dims.width, dims.height);
			const followedByGroup = items[itemIndex + 1]?.kind === "group";
			const zoneShrink = Math.min(1, space.u * (followedByGroup ? .62 : .9) / flow.uSize, space.v * .66 / flow.vSize);
			const uSize = flow.uSize * zoneShrink;
			const vSize = flow.vSize * zoneShrink;
			const u = items.length === 1 ? (space.u - uSize) / 2 : followedByGroup ? 0 : ownBandOnEndSide ? space.u - uSize : 0;
			placements.push({
				box: zone,
				rect: {
					u,
					v: vCursor,
					uSize,
					vSize
				},
				scale: dims.baseScale * globalScale * zoneShrink,
				vertical: dims.useVertical
			});
			if (followedByGroup) floats.push({
				extent: uSize + chipGap,
				vBottom: vCursor + vSize + lineGap
			});
			else {
				vCursor += vSize + lineGap;
				ownBandOnEndSide = !ownBandOnEndSide;
			}
		});
		return {
			placements,
			vTotal: placements.reduce((max, placement) => Math.max(max, placement.rect.v + placement.rect.vSize), 0)
		};
	};
	const layoutSonnetPosterBlocks = (boxes, width, height, baseFontSize, seed = 0) => {
		if (boxes.length === 0) return {
			placements: [],
			width: 0,
			height: 0,
			gap: 0
		};
		const gap = clamp$2(baseFontSize * .35, 16, 40);
		const chipGap = gap;
		const lineGap = gap * 1.15;
		const canvas = {
			x: -width * .42,
			y: -height * .4,
			width: width * .84,
			height: height * .8
		};
		const orientation = seed % 2 === 0 ? "horizontal" : "vertical";
		const space = orientation === "horizontal" ? {
			orientation,
			u: canvas.width,
			v: canvas.height
		} : {
			orientation,
			u: canvas.height,
			v: canvas.width
		};
		let attempt = attemptFlowLayout(boxes, space, 1, chipGap, lineGap, seed);
		for (const globalScale of [
			.92,
			.84,
			.76,
			.68,
			.6,
			.52
		]) {
			if (attempt.vTotal <= space.v + .5) break;
			attempt = attemptFlowLayout(boxes, space, globalScale, chipGap, lineGap, seed);
		}
		if (attempt.vTotal > space.v) {
			const fitScale = space.v / attempt.vTotal;
			attempt.placements.forEach((placement) => {
				placement.rect.u *= fitScale;
				placement.rect.v *= fitScale;
				placement.rect.uSize *= fitScale;
				placement.rect.vSize *= fitScale;
				placement.scale *= fitScale;
			});
			attempt.vTotal = space.v;
		}
		const vShift = Math.max(0, (space.v - attempt.vTotal) / 2);
		attempt.placements.forEach((placement) => {
			const { box } = placement;
			const rect = {
				...placement.rect,
				v: placement.rect.v + vShift
			};
			const screen = flowToScreen(space, rect, canvas);
			box.fontScale = placement.scale;
			box.measuredWidth = screen.width;
			box.measuredHeight = screen.height;
			box.x = screen.x + screen.width / 2;
			box.y = screen.y + screen.height / 2;
			box.rotation = 0;
			box.vertical = placement.vertical;
			if (placement.vertical && box.verticalDisplayText) box.displayText = box.verticalDisplayText;
			box.layoutDirection = orientation === "vertical" ? "vertical" : "horizontal";
			if (orientation === "horizontal") {
				box.enterX = (screen.x + screen.width / 2 < 0 ? -1 : 1) * Math.min(28, baseFontSize * .45);
				box.enterY = Math.min(18, baseFontSize * .25);
			} else {
				box.enterX = Math.min(18, baseFontSize * .25);
				box.enterY = (screen.y + screen.height / 2 < 0 ? -1 : 1) * Math.min(28, baseFontSize * .45);
			}
		});
		return {
			placements: boxes,
			width: canvas.width,
			height: canvas.height,
			gap
		};
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/sonnet/sonnetShotFlowLayouts.ts
	const clamp$1 = (value, min, max) => Math.min(max, Math.max(min, value));
	const resolveSonnetFlowGaps = (baseFontSize) => {
		const flowGap = clamp$1(baseFontSize * .35, 16, 40);
		return {
			flowGap,
			stackGap: Math.max(24, flowGap * 1.35)
		};
	};
	const placeWithGlobalFit = (ctx, place) => {
		const snapshot = ctx.boxes.map((box) => ({
			fontScale: box.fontScale,
			measuredWidth: box.measuredWidth,
			measuredHeight: box.measuredHeight
		}));
		const safeHalfW = ctx.width * .48;
		const safeHalfH = ctx.height * .46;
		for (const globalScale of [
			1,
			.92,
			.84,
			.76,
			.68,
			.6,
			.52
		]) {
			ctx.boxes.forEach((box, index) => {
				box.fontScale = snapshot[index].fontScale * globalScale;
				box.measuredWidth = snapshot[index].measuredWidth * globalScale;
				box.measuredHeight = snapshot[index].measuredHeight * globalScale;
			});
			place(globalScale);
			if (ctx.boxes.every((box) => Math.abs(box.x) + box.measuredWidth / 2 <= safeHalfW + .5 && Math.abs(box.y) + box.measuredHeight / 2 <= safeHalfH + .5)) return;
		}
	};
	const layoutQuietTableau = (ctx, variant) => {
		const { boxes, heroIndex, height, stackGap } = ctx;
		const heroBox = boxes[heroIndex];
		const horizontalCard = variant === 2 || variant === 3;
		boxes.forEach((box) => {
			box.layoutDirection = horizontalCard ? "horizontal" : "vertical";
		});
		const safeHalfH = height * .46;
		placeWithGlobalFit(ctx, () => {
			heroBox.x = 0;
			heroBox.y = horizontalCard ? 0 : -height * .1;
			const stagger = variant === 3 ? 70 : 0;
			const columnStep = Math.max(...boxes.map((box) => box.measuredWidth)) + stackGap + stagger;
			const xFor = (box, index) => {
				if (variant === 1) return heroBox.x - heroBox.measuredWidth / 2 + box.measuredWidth / 2;
				if (variant === 3) return heroBox.x + (index % 2 === 0 ? 1 : -1) * 35;
				return heroBox.x;
			};
			let column = 0;
			let currentY = heroBox.y - heroBox.measuredHeight / 2 - stackGap;
			for (let i = heroIndex - 1; i >= 0; i--) {
				const box = boxes[i];
				if (currentY - box.measuredHeight < -safeHalfH) {
					column += 1;
					currentY = safeHalfH;
				}
				box.x = xFor(box, i) + column * columnStep;
				box.y = currentY - box.measuredHeight / 2;
				currentY -= box.measuredHeight + stackGap;
				if (variant === 1) {
					box.enterX = 20;
					box.enterY = 0;
				} else if (variant === 3) {
					box.enterX = box.x > heroBox.x ? 30 : -30;
					box.enterY = 0;
				} else {
					box.enterX = 0;
					box.enterY = 20;
				}
			}
			column = 0;
			currentY = heroBox.y + heroBox.measuredHeight / 2 + stackGap;
			for (let i = heroIndex + 1; i < boxes.length; i++) {
				const box = boxes[i];
				if (currentY + box.measuredHeight > safeHalfH) {
					column += 1;
					currentY = -safeHalfH;
				}
				box.x = xFor(box, i) - column * columnStep;
				box.y = currentY + box.measuredHeight / 2;
				currentY += box.measuredHeight + stackGap;
				if (variant === 1) {
					box.enterX = -20;
					box.enterY = 0;
				} else if (variant === 3) {
					box.enterX = box.x > heroBox.x ? 30 : -30;
					box.enterY = 0;
				} else {
					box.enterX = 0;
					box.enterY = -20;
				}
			}
		});
	};
	const layoutTrackingRibbon = (ctx, variant) => {
		const { boxes, heroIndex, flowGap } = ctx;
		const heroBox = boxes[heroIndex];
		boxes.forEach((box) => {
			box.layoutDirection = "horizontal";
		});
		placeWithGlobalFit(ctx, () => {
			heroBox.x = 0;
			heroBox.y = 0;
			const alignY = (box, index) => variant === 1 ? heroBox.y + heroBox.measuredHeight / 2 - box.measuredHeight / 2 : variant === 2 ? heroBox.y - heroBox.measuredHeight / 2 + box.measuredHeight / 2 : heroBox.y + (index % 2 === 0 ? 10 : -10);
			const enter = variant === 2 ? 20 : 30;
			let currentX = heroBox.x - heroBox.measuredWidth / 2 - flowGap;
			for (let i = heroIndex - 1; i >= 0; i--) {
				const box = boxes[i];
				box.x = currentX - box.measuredWidth / 2;
				box.y = alignY(box, i);
				currentX -= box.measuredWidth + flowGap;
				box.enterX = enter;
				box.enterY = 0;
			}
			currentX = heroBox.x + heroBox.measuredWidth / 2 + flowGap;
			for (let i = heroIndex + 1; i < boxes.length; i++) {
				const box = boxes[i];
				box.x = currentX + box.measuredWidth / 2;
				box.y = alignY(box, i);
				currentX += box.measuredWidth + flowGap;
				box.enterX = -enter;
				box.enterY = 0;
			}
		});
	};
	const layoutEditorialColumn = (ctx, variant, secondaryHeroIndex) => {
		const { boxes, heroIndex, width, height, flowGap, stackGap } = ctx;
		const heroBox = boxes[heroIndex];
		if (variant === 0) {
			boxes.forEach((box) => {
				box.layoutDirection = "vertical";
			});
			placeWithGlobalFit(ctx, () => {
				heroBox.x = -width * .15;
				heroBox.y = 0;
				let currentY = heroBox.y - heroBox.measuredHeight / 2 + stackGap * .5;
				for (let i = 0; i < heroIndex; i++) {
					const box = boxes[i];
					box.x = heroBox.x + heroBox.measuredWidth / 2 + flowGap + box.measuredWidth / 2;
					box.y = currentY + box.measuredHeight / 2;
					currentY += box.measuredHeight + stackGap;
					box.enterX = -20;
					box.enterY = 0;
				}
				currentY = heroBox.y - heroBox.measuredHeight / 2 + stackGap * .5;
				for (let i = heroIndex + 1; i < boxes.length; i++) {
					const box = boxes[i];
					box.x = heroBox.x - heroBox.measuredWidth / 2 - flowGap - box.measuredWidth / 2;
					box.y = currentY + box.measuredHeight / 2;
					currentY += box.measuredHeight + stackGap;
					box.enterX = 20;
					box.enterY = 0;
				}
			});
		} else if (variant === 1) {
			boxes.forEach((box) => {
				box.layoutDirection = "vertical";
			});
			placeWithGlobalFit(ctx, () => {
				const rightEdge = width * .28;
				const safeHalfH = height * .46;
				const railStep = Math.max(...boxes.map((box) => box.measuredWidth)) + stackGap;
				const totalHeight = boxes.reduce((sum, box) => sum + box.measuredHeight, 0) + stackGap * (boxes.length - 1);
				if (boxes.reduce((sum, box) => sum + box.measuredHeight, 0) * .52 + stackGap * (boxes.length - 1) <= safeHalfH * 2) {
					let currentY = -totalHeight / 2;
					boxes.forEach((box) => {
						box.x = rightEdge - box.measuredWidth / 2;
						box.y = currentY + box.measuredHeight / 2;
						currentY += box.measuredHeight + stackGap;
						box.enterX = 20;
						box.enterY = 0;
					});
					return;
				}
				let rail = 0;
				let currentY = -safeHalfH;
				boxes.forEach((box) => {
					if (currentY + box.measuredHeight > safeHalfH) {
						rail += 1;
						currentY = -safeHalfH;
					}
					box.x = rightEdge - rail * railStep - box.measuredWidth / 2;
					box.y = currentY + box.measuredHeight / 2;
					currentY += box.measuredHeight + stackGap;
					box.enterX = 20;
					box.enterY = 0;
				});
			});
		} else if (variant === 2) {
			boxes.forEach((box) => {
				box.layoutDirection = "horizontal";
			});
			placeWithGlobalFit(ctx, () => {
				heroBox.x = 0;
				heroBox.y = -height * .25;
				const before = boxes.slice(0, heroIndex);
				const after = boxes.slice(heroIndex + 1);
				if (before.length > 0) {
					const kickerHeight = Math.max(...before.map((box) => box.measuredHeight));
					const kickerWidth = before.reduce((sum, box) => sum + box.measuredWidth, 0) + flowGap * (before.length - 1);
					const kickerY = heroBox.y - heroBox.measuredHeight / 2 - stackGap - kickerHeight / 2;
					let currentX = heroBox.x - kickerWidth / 2;
					before.forEach((box) => {
						box.x = currentX + box.measuredWidth / 2;
						box.y = kickerY;
						currentX += box.measuredWidth + flowGap;
						box.enterX = 0;
						box.enterY = -20;
					});
				}
				const leftAnchor = heroBox.x - heroBox.measuredWidth * .25 - flowGap;
				const rightAnchor = heroBox.x + heroBox.measuredWidth * .25 + flowGap;
				let currentY = heroBox.y + heroBox.measuredHeight / 2 + stackGap;
				for (let pair = 0; pair < after.length; pair += 2) {
					const left = after[pair];
					const right = after[pair + 1];
					const rowHeight = Math.max(left.measuredHeight, right?.measuredHeight ?? 0);
					left.x = leftAnchor - left.measuredWidth / 2;
					left.y = currentY + left.measuredHeight / 2;
					left.enterX = -20;
					left.enterY = 0;
					if (right) {
						right.x = rightAnchor + right.measuredWidth / 2;
						right.y = currentY + right.measuredHeight / 2;
						right.enterX = 20;
						right.enterY = 0;
					}
					currentY += rowHeight + stackGap;
				}
			});
		} else if (variant === 3) {
			boxes.forEach((box) => {
				box.layoutDirection = "horizontal";
			});
			placeWithGlobalFit(ctx, () => {
				heroBox.x = 0;
				heroBox.y = 0;
				const firstHero = Math.min(heroIndex, secondaryHeroIndex);
				const line1 = boxes.slice(0, firstHero + 1);
				const line2 = boxes.slice(firstHero + 1);
				const line1Height = Math.max(...line1.map((box) => box.measuredHeight));
				const line2Height = Math.max(...line2.map((box) => box.measuredHeight));
				const totalHeight = line1Height + stackGap + line2Height;
				const line1Y = heroBox.y - totalHeight / 2 + line1Height / 2;
				const line2Y = line1Y + line1Height / 2 + stackGap + line2Height / 2;
				const layLine = (line, lineY, enterX) => {
					const lineWidth = line.reduce((sum, box) => sum + box.measuredWidth, 0) + flowGap * (line.length - 1);
					let currentX = -lineWidth / 2;
					line.forEach((box) => {
						box.x = currentX + box.measuredWidth / 2;
						box.y = lineY;
						currentX += box.measuredWidth + flowGap;
						box.enterX = enterX;
						box.enterY = 0;
					});
					return lineWidth;
				};
				const line1Width = layLine(line1, line1Y, 30);
				const line2Width = layLine(line2, line2Y, -30);
				const offsetAmount = Math.max(line1Width, line2Width) * .12;
				line1.forEach((box) => {
					box.x -= offsetAmount;
				});
				line2.forEach((box) => {
					box.x += offsetAmount;
				});
			});
		} else if (variant === 4) {
			boxes.forEach((box, index) => {
				box.layoutDirection = index === heroIndex ? "vertical" : "horizontal";
			});
			placeWithGlobalFit(ctx, () => {
				const heroOnRight = heroIndex === boxes.length - 1;
				const blockLeft = -width * .4;
				const blockRight = width * .4;
				let currentY = -height * .34;
				const flowWords = (indices, regionFor) => {
					let [left, right] = regionFor(currentY);
					let currentX = left;
					let rowHeight = 0;
					indices.forEach((index) => {
						const box = boxes[index];
						if (currentX > left && currentX + box.measuredWidth > right) {
							currentY += rowHeight + stackGap;
							[left, right] = regionFor(currentY);
							currentX = left;
							rowHeight = 0;
						}
						box.x = currentX + box.measuredWidth / 2;
						box.y = currentY + box.measuredHeight / 2;
						box.enterX = heroOnRight ? -25 : 25;
						box.enterY = 0;
						currentX += box.measuredWidth + flowGap;
						rowHeight = Math.max(rowHeight, box.measuredHeight);
					});
					if (indices.length > 0) currentY += rowHeight;
				};
				const beforeIndices = boxes.slice(0, heroIndex).map((box) => box.index);
				const afterIndices = boxes.slice(heroIndex + 1).map((box) => box.index);
				flowWords(beforeIndices, () => [blockLeft, blockRight]);
				currentY += stackGap;
				const pillarLeft = heroOnRight ? blockRight - heroBox.measuredWidth : blockLeft;
				heroBox.x = pillarLeft + heroBox.measuredWidth / 2;
				heroBox.y = currentY + heroBox.measuredHeight / 2;
				const pillarBottom = currentY + heroBox.measuredHeight + stackGap;
				const besideLeft = heroOnRight ? blockLeft : pillarLeft + heroBox.measuredWidth + flowGap;
				const besideRight = heroOnRight ? pillarLeft - flowGap : blockRight;
				flowWords(afterIndices, (rowTop) => rowTop < pillarBottom - .5 ? [besideLeft, besideRight] : [blockLeft, blockRight]);
			});
		}
	};
	const layoutFragmentCollage = (ctx, variant) => {
		const { boxes, heroIndex, flowGap, stackGap } = ctx;
		const heroBox = boxes[heroIndex];
		const rectSeparation = (a, b) => Math.max(Math.max(a.left - b.right, b.left - a.right), Math.max(a.top - b.bottom, b.top - a.bottom));
		boxes.forEach((box, index) => {
			if (index === heroIndex) return;
			if (Math.abs(Math.round(box.rotation / (Math.PI / 2)) % 2) === 1) {
				const rotatedWidth = box.measuredHeight;
				box.measuredHeight = box.measuredWidth;
				box.measuredWidth = rotatedWidth;
			}
			box.rotation = 0;
		});
		placeWithGlobalFit(ctx, (globalScale) => {
			heroBox.x = 0;
			heroBox.y = 0;
			const baseRadius = Math.hypot(heroBox.measuredWidth, heroBox.measuredHeight) / 2 + stackGap;
			const count = Math.max(1, boxes.length - 1);
			const squash = .65;
			const placed = [{
				left: heroBox.x - heroBox.measuredWidth / 2,
				right: heroBox.x + heroBox.measuredWidth / 2,
				top: heroBox.y - heroBox.measuredHeight / 2,
				bottom: heroBox.y + heroBox.measuredHeight / 2
			}];
			let angle = Math.PI / 4;
			let supportIndex = 0;
			for (let i = 0; i < boxes.length; i++) {
				if (i === heroIndex) continue;
				const box = boxes[i];
				let radius = baseRadius;
				if (variant === 1) radius += (35 + supportIndex / count * 150) * globalScale;
				else if (variant === 2) radius += (supportIndex % 2 === 1 ? 140 : 50) * globalScale;
				else radius += (45 + supportIndex * 23 % 90) * globalScale;
				supportIndex += 1;
				let candidate = angle;
				let rect = {
					left: 0,
					right: 0,
					top: 0,
					bottom: 0
				};
				let resolvedRadius = radius;
				let placedClear = false;
				for (let ring = 0; ring < 14 && !placedClear; ring += 1) {
					for (let attempt = 0; attempt < 400; attempt++) {
						rect = {
							left: Math.cos(candidate) * resolvedRadius - box.measuredWidth / 2,
							right: Math.cos(candidate) * resolvedRadius + box.measuredWidth / 2,
							top: Math.sin(candidate) * resolvedRadius * squash - box.measuredHeight / 2,
							bottom: Math.sin(candidate) * resolvedRadius * squash + box.measuredHeight / 2
						};
						if (placed.every((entry) => rectSeparation(entry, rect) >= flowGap)) {
							placedClear = true;
							break;
						}
						candidate += .07;
					}
					if (!placedClear) resolvedRadius += (36 + ring * 12) * globalScale;
				}
				angle = candidate + .02;
				placed.push(rect);
				box.x = heroBox.x + Math.cos(candidate) * resolvedRadius;
				box.y = heroBox.y + Math.sin(candidate) * resolvedRadius * squash;
				box.layoutDirection = Math.abs(Math.cos(candidate)) >= Math.abs(Math.sin(candidate)) ? "vertical" : "horizontal";
				box.enterX = Math.cos(candidate) * -60;
				box.enterY = Math.sin(candidate) * -60;
			}
		});
	};
	const layoutCrossStack = (ctx) => {
		const { boxes, heroIndex, height, flowGap, stackGap } = ctx;
		const heroBox = boxes[heroIndex];
		const topCount = Math.floor(heroIndex / 2);
		const afterCount = boxes.length - 1 - heroIndex;
		const rightCount = Math.ceil(afterCount / 2);
		const fillColumn = (column) => {
			if (column.length === 0) return 0;
			const available = Math.max(0, height * .46 - heroBox.measuredHeight / 2 - stackGap);
			if (available <= 0) return 0;
			const gaps = stackGap * (column.length - 1);
			const contentHeight = column.reduce((sum, box) => sum + box.measuredHeight, 0);
			const target = available * .72;
			if (contentHeight + gaps < target) {
				const boost = Math.min(2.2, (target - gaps) / Math.max(1, contentHeight));
				column.forEach((box) => {
					const capped = Math.min(boost, heroBox.fontScale * .6 / box.fontScale);
					if (capped > 1.05) {
						box.fontScale *= capped;
						box.measuredWidth *= capped;
						box.measuredHeight *= capped;
					}
				});
			}
			if (column.length < 2) return 0;
			const grown = column.reduce((sum, box) => sum + box.measuredHeight, 0);
			const pitch = (available * .95 - grown) / (column.length - 1);
			return Math.max(0, Math.min(stackGap * 2, pitch - stackGap));
		};
		placeWithGlobalFit(ctx, () => {
			heroBox.x = 0;
			heroBox.y = 0;
			const topStretch = fillColumn(boxes.slice(0, topCount));
			const bottomStretch = fillColumn(boxes.slice(heroIndex + rightCount + 1));
			let currentX = heroBox.x - heroBox.measuredWidth / 2 - stackGap;
			for (let i = heroIndex - 1; i >= topCount; i--) {
				const box = boxes[i];
				box.layoutDirection = "horizontal";
				box.x = currentX - box.measuredWidth / 2;
				box.y = heroBox.y + (i % 2 === 0 ? 10 : -10);
				currentX -= box.measuredWidth + flowGap;
				box.enterX = -30;
				box.enterY = 0;
			}
			let currentY = heroBox.y - heroBox.measuredHeight / 2 - stackGap;
			for (let i = topCount - 1; i >= 0; i--) {
				const box = boxes[i];
				box.layoutDirection = "vertical";
				box.x = heroBox.x + (i % 2 === 0 ? 15 : -15);
				box.y = currentY - box.measuredHeight / 2;
				currentY -= box.measuredHeight + stackGap + topStretch;
				box.enterX = 0;
				box.enterY = -30;
			}
			currentX = heroBox.x + heroBox.measuredWidth / 2 + stackGap;
			for (let i = heroIndex + 1; i <= heroIndex + rightCount; i++) {
				const box = boxes[i];
				box.layoutDirection = "horizontal";
				box.x = currentX + box.measuredWidth / 2;
				box.y = heroBox.y + (i % 2 === 0 ? 10 : -10);
				currentX += box.measuredWidth + flowGap;
				box.enterX = 30;
				box.enterY = 0;
			}
			currentY = heroBox.y + heroBox.measuredHeight / 2 + stackGap;
			for (let i = heroIndex + rightCount + 1; i < boxes.length; i++) {
				const box = boxes[i];
				box.layoutDirection = "vertical";
				box.x = heroBox.x + (i % 2 === 0 ? 15 : -15);
				box.y = currentY + box.measuredHeight / 2;
				currentY += box.measuredHeight + stackGap + bottomStretch;
				box.enterX = 0;
				box.enterY = 30;
			}
		});
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/sonnet/sonnetTypographyLayout.ts
	const CJK_TEXT = /[\u4e00-\u9fff\u3040-\u30ff\uac00-\ud7af]/u;
	const shouldRotateNonCjkSegment = (segment, vertical) => vertical && segment.graphemes.filter((item) => item.char.trim().length > 0).length > 1 && !CJK_TEXT.test(segment.text);
	const verticalText = (segment) => (segment.graphemes.length ? segment.graphemes.map((item) => item.char) : Array.from(segment.text)).join("\n");
	/**
	* Measurement is memoised across every caller. `fontSpec` names the weight, size and family, so
	* a key is fully self-describing and two entries with the same key can never disagree. Without
	* it every scene re-measured the same graphemes from scratch through pretext - per character,
	* per shot, per paragraph - which is the bulk of what a scene build costs.
	*/
	const MEASURE_CACHE_LIMIT = 2e4;
	const measureCache = /* @__PURE__ */ new Map();
	const measureText = (text, fontSpec, fontSize) => {
		const key = `${fontSpec}|${fontSize}|${text}`;
		const cached = measureCache.get(key);
		if (cached !== void 0) return cached;
		let width;
		try {
			width = layoutWithLines(prepareWithSegments(text || " ", fontSpec), 99999, fontSize * 1.2).lines[0]?.width ?? text.length * fontSize * .6;
		} catch {
			width = text.length * fontSize * .6;
		}
		if (measureCache.size >= MEASURE_CACHE_LIMIT) {
			const oldest = measureCache.keys().next();
			if (!oldest.done) measureCache.delete(oldest.value);
		}
		measureCache.set(key, width);
		return width;
	};
	const resolveSonnetTypographyLayout = ({ lines, shotKind, paragraphKind, width, height, baseFontSize, fontFamily, fontWeight }) => {
		const segments = lines.flat();
		let offset = 0;
		const heroIndices = [];
		const semiHeroIndices = [];
		lines.forEach((lineSegs) => {
			const localHero = findSonnetHeroSegmentIndex(lineSegs);
			const globalHero = offset + localHero;
			const localSemiHeroes = findSonnetSemiHeroSegmentIndices(lineSegs, localHero);
			heroIndices.push(globalHero);
			localSemiHeroes.forEach((localSemiHero) => semiHeroIndices.push(offset + localSemiHero));
			offset += lineSegs.length;
		});
		const heroIndex = findSonnetHeroSegmentIndex(segments);
		const midpoints = segments.map((segment) => (segment.startTime + segment.endTime) / 2);
		const timelineStart = Math.min(...midpoints);
		const timelineDuration = Math.max(...midpoints) - timelineStart;
		const phases = midpoints.map((midpoint, index) => timelineDuration > .001 ? (midpoint - timelineStart) / timelineDuration : index / Math.max(1, segments.length - 1));
		const heroPhase = phases[heroIndex] ?? .5;
		const layoutVariantSeed = segments.reduce((acc, seg) => acc + (seg.text.trim().length || 1), 0) + segments.length;
		const posterLayoutSeed = hashVisualizerSeed(segments.map((segment) => segment.text).join("␟"));
		let editorialVariant = layoutVariantSeed % 5;
		const ribbonVariant = layoutVariantSeed % 3;
		const tableauVariant = layoutVariantSeed % 4;
		const collageVariant = layoutVariantSeed % 3;
		let secondaryHeroIndex = -1;
		if (editorialVariant === 3 && segments.length > 2) {
			let bestScore = -Infinity;
			segments.forEach((segment, index) => {
				if (index === heroIndex || !segment.isWordLike || getSonnetVisibleSegmentLength(segment) === 0) return;
				const distanceBonus = Math.abs(index - heroIndex) > 1 ? 50 : 0;
				const score = scoreSonnetHeroSegment(segment) + distanceBonus;
				if (score > bestScore) {
					bestScore = score;
					secondaryHeroIndex = index;
				}
			});
			if (secondaryHeroIndex === -1) editorialVariant = 0;
		} else if (editorialVariant === 3) editorialVariant = 0;
		else if (editorialVariant === 4 && segments.length < 2) editorialVariant = 2;
		const boxes = segments.map((segment, index) => {
			const isHero = heroIndices.includes(index) || index === secondaryHeroIndex && shotKind === "editorial-column" && editorialVariant === 3;
			const isSemiHero = semiHeroIndices.includes(index) && !isHero;
			const isEmphasized = isHero || isSemiHero;
			let heroFontScale = 1;
			let supportFontScale = 1;
			let vertical = false;
			let rotation = 0;
			switch (shotKind) {
				case "editorial-column":
					if (editorialVariant === 3) {
						heroFontScale = 3.8;
						supportFontScale = 1.3;
						vertical = false;
					} else if (editorialVariant === 4) {
						heroFontScale = 4.2;
						supportFontScale = 1.25;
						vertical = isEmphasized;
					} else {
						heroFontScale = editorialVariant === 2 ? 3.2 : 4;
						supportFontScale = 1.2;
						vertical = isEmphasized && editorialVariant !== 2;
					}
					break;
				case "type-impact":
					heroFontScale = 5.5;
					supportFontScale = 1.5;
					break;
				case "fragment-collage":
					heroFontScale = 3.2;
					supportFontScale = 1.35;
					vertical = isSemiHero || index % 4 === 0;
					break;
				case "tracking-ribbon":
					heroFontScale = 3.5;
					supportFontScale = 1.5;
					break;
				case "mask-reveal":
					heroFontScale = 4.5;
					supportFontScale = 1.6;
					vertical = isEmphasized;
					break;
				case "poster-blocks":
					heroFontScale = 4.4;
					supportFontScale = 1.15;
					break;
				default:
					heroFontScale = 3;
					supportFontScale = 1.15;
					vertical = isEmphasized && (tableauVariant === 0 || tableauVariant === 1);
			}
			let fontScale = isHero ? heroFontScale : isSemiHero ? Math.max(supportFontScale * 1.35, heroFontScale * .72) : supportFontScale;
			const rotatesNonCjkSegment = shouldRotateNonCjkSegment(segment, vertical);
			if (rotatesNonCjkSegment) {
				vertical = false;
				rotation += Math.PI / 2;
			}
			const displayText = vertical ? verticalText(segment) : segment.text;
			const renderWeight = resolveSonnetRoleFontWeight(fontWeight, isHero ? "hero" : isSemiHero ? "semi-hero" : "support");
			let targetFontSize = baseFontSize * fontScale;
			const fontSpec = `${renderWeight} ${targetFontSize}px ${fontFamily}`;
			const horizontalAdvance = rotatesNonCjkSegment ? segment.graphemes.reduce((sum, item) => item.char.trim().length > 0 ? sum + Math.max(targetFontSize * .2, measureText(item.char, fontSpec, targetFontSize)) : sum, 0) : measureText(displayText, fontSpec, targetFontSize);
			let measuredWidth = rotatesNonCjkSegment ? targetFontSize * 1.2 : horizontalAdvance;
			let measuredHeight = rotatesNonCjkSegment ? horizontalAdvance : targetFontSize * 1.2;
			if (vertical) {
				const columnChars = segment.graphemes.length ? segment.graphemes.map((item) => item.char) : Array.from(segment.text);
				const glyphAdvances = columnChars.filter((char) => char.trim().length > 0).map((char) => Math.max(targetFontSize * .2, measureText(char, fontSpec, targetFontSize)));
				measuredWidth = glyphAdvances.length ? Math.max(...glyphAdvances) : targetFontSize;
				measuredHeight = Math.max(1, columnChars.length) * targetFontSize * .9;
			}
			const maxW = width * .82;
			const maxH = height * .82;
			let fitScale = 1;
			if (measuredWidth > maxW) fitScale = Math.min(fitScale, maxW / measuredWidth);
			if (measuredHeight > maxH) fitScale = Math.min(fitScale, maxH / measuredHeight);
			if (fitScale < 1) {
				targetFontSize *= fitScale;
				fontScale *= fitScale;
				measuredWidth *= fitScale;
				measuredHeight *= fitScale;
			}
			let posterVerticalDisplayText;
			let posterVerticalMeasuredWidth;
			let posterVerticalMeasuredHeight;
			let posterVerticalFontScale;
			if (shotKind === "poster-blocks" && CJK_TEXT.test(segment.text)) {
				const columnChars = segment.graphemes.length ? segment.graphemes.map((item) => item.char) : Array.from(segment.text);
				const glyphAdvances = columnChars.filter((char) => char.trim().length > 0).map((char) => Math.max(targetFontSize * .2, measureText(char, fontSpec, targetFontSize)));
				let columnWidth = glyphAdvances.length ? Math.max(...glyphAdvances) : targetFontSize;
				let columnHeight = Math.max(1, columnChars.length) * targetFontSize * .9;
				const verticalFit = Math.min(1, maxW / columnWidth, maxH / columnHeight);
				columnWidth *= verticalFit;
				columnHeight *= verticalFit;
				posterVerticalDisplayText = verticalText(segment);
				posterVerticalMeasuredWidth = columnWidth;
				posterVerticalMeasuredHeight = columnHeight;
				posterVerticalFontScale = fontScale * verticalFit;
			}
			return {
				index,
				isHero,
				isSemiHero,
				displayText,
				verticalDisplayText: posterVerticalDisplayText,
				verticalMeasuredWidth: posterVerticalMeasuredWidth,
				verticalMeasuredHeight: posterVerticalMeasuredHeight,
				verticalFontScale: posterVerticalFontScale,
				fontScale,
				vertical,
				layoutDirection: "horizontal",
				rotation,
				measuredWidth,
				measuredHeight,
				timingPhase: phases[index],
				relativePhase: phases[index] - heroPhase,
				role: void 0,
				x: 0,
				y: 0,
				enterX: 0,
				enterY: 0
			};
		});
		const heroBox = boxes[heroIndex];
		if (heroBox) {
			if (shotKind === "poster-blocks") layoutSonnetPosterBlocks(boxes, width, height, baseFontSize, posterLayoutSeed);
			else {
				const { flowGap, stackGap } = resolveSonnetFlowGaps(baseFontSize);
				const flowCtx = {
					boxes,
					heroIndex,
					width,
					height,
					flowGap,
					stackGap
				};
				if (shotKind === "quiet-tableau") layoutQuietTableau(flowCtx, tableauVariant);
				else if (shotKind === "tracking-ribbon") layoutTrackingRibbon(flowCtx, ribbonVariant);
				else if (shotKind === "editorial-column") layoutEditorialColumn(flowCtx, editorialVariant, secondaryHeroIndex);
				else if (shotKind === "fragment-collage") layoutFragmentCollage(flowCtx, collageVariant);
				else layoutCrossStack(flowCtx);
			}
			heroBox.enterX = 0;
			heroBox.enterY = height * .15;
			const decorations = [];
			if (shotKind !== "quiet-tableau" && shotKind !== "poster-blocks") {
				const allHeroes = boxes.filter((b) => b.isHero);
				allHeroes.forEach((hBox, idx) => {
					decorations.push({
						...hBox,
						isHero: false,
						role: "decoration",
						fontScale: Math.max(2.8, Math.min(hBox.fontScale * 3.5, 5.5)),
						vertical: false,
						x: hBox.x - width * (.1 - idx * .03),
						y: hBox.y - height * (.05 - idx * .02),
						rotation: -.15 + (idx % 2 === 0 ? 0 : .05),
						enterX: -width * .05,
						enterY: -height * .05
					});
				});
				if (boxes.length > 1 && allHeroes.length > 0) {
					const dec2 = boxes[boxes.length - 1].isHero ? boxes[0] : boxes[boxes.length - 1];
					decorations.push({
						...dec2,
						isHero: false,
						role: "decoration",
						fontScale: Math.max(1.8, Math.min(allHeroes[0].fontScale * 2.2, 3.5)),
						vertical: false,
						x: allHeroes[0].x + width * .25,
						y: allHeroes[0].y + height * .15,
						rotation: .08,
						enterX: width * .05,
						enterY: height * .05
					});
				}
			}
			boxes.unshift(...decorations);
		}
		return boxes.map((box) => ({
			segmentIndex: box.index,
			displayText: box.displayText,
			role: box.role || (box.isHero ? "hero" : box.isSemiHero ? "semi-hero" : "support"),
			fontScale: box.fontScale,
			measuredWidth: box.measuredWidth,
			measuredHeight: box.measuredHeight,
			x: box.x,
			y: box.y,
			rotation: box.rotation,
			enterX: box.enterX,
			enterY: box.enterY,
			vertical: box.vertical,
			layoutDirection: box.layoutDirection,
			timingPhase: box.timingPhase
		}));
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/sonnet/sonnetGlyphLayout.ts
	const resolveSonnetGlyphMotionDuration = (window) => {
		const shotDuration = Math.max(.001, window.endTime - window.startTime);
		const preferred = Math.min(1.8, Math.max(.65, shotDuration * .42));
		return Math.min(preferred, shotDuration * .72);
	};
	const buildSonnetGlyphLayout = (segment, placement, fontSize, measureGlyph, motionWindow) => {
		const fallbackChars = Array.from(segment.text);
		const graphemes = segment.graphemes.length ? segment.graphemes : fallbackChars.map((char, index) => ({
			char,
			startTime: segment.startTime + (segment.endTime - segment.startTime) * index / Math.max(1, fallbackChars.length),
			endTime: segment.startTime + (segment.endTime - segment.startTime) * (index + 1) / Math.max(1, fallbackChars.length)
		}));
		const advances = graphemes.map((item) => placement.vertical ? fontSize * .9 : Math.max(fontSize * .2, measureGlyph(item.char)));
		const totalAdvance = advances.reduce((sum, advance) => sum + advance, 0);
		const motionDuration = resolveSonnetGlyphMotionDuration(motionWindow);
		let cursor = -totalAdvance / 2;
		return graphemes.map((grapheme, index) => {
			const advance = advances[index];
			const localX = placement.vertical ? 0 : cursor + advance / 2;
			const localY = placement.vertical ? cursor + advance / 2 : 0;
			cursor += advance;
			const cosine = Math.cos(placement.rotation);
			const sine = Math.sin(placement.rotation);
			const stagger = index % 2 === 0 ? -1 : 1;
			const startTime = grapheme.startTime;
			const settleTime = startTime + motionDuration;
			return {
				char: grapheme.char,
				baseX: placement.x + localX * cosine - localY * sine,
				baseY: placement.y + localX * sine + localY * cosine,
				enterX: placement.enterX + (placement.vertical ? stagger * fontSize * .28 : 0),
				enterY: placement.enterY + (placement.vertical ? 0 : stagger * fontSize * .24),
				entryRotation: stagger * (isSonnetEmphasisRole(placement.role) ? .055 : .035),
				startTime,
				settleTime: Math.max(startTime, settleTime)
			};
		});
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/sonnet/sonnetMotion.ts
	const clamp01$1 = (value) => Math.min(1, Math.max(0, value));
	const cubicCoordinate = (point1, point2, time) => {
		const inverse = 1 - time;
		return 3 * inverse * inverse * time * point1 + 3 * inverse * time * time * point2 + time * time * time;
	};
	const resolveCubicBezier = (x1, y1, x2, y2, value) => {
		const target = clamp01$1(value);
		if (target === 0 || target === 1) return target;
		let low = 0;
		let high = 1;
		let parameter = target;
		for (let iteration = 0; iteration < 12; iteration += 1) {
			if (cubicCoordinate(x1, x2, parameter) < target) low = parameter;
			else high = parameter;
			parameter = (low + high) / 2;
		}
		return cubicCoordinate(y1, y2, parameter);
	};
	const easeSonnetInOut = (value) => resolveCubicBezier(.65, 0, .35, 1, value);
	const easeSonnetExpoOut = (value) => value === 1 ? 1 : 1 - Math.pow(2, -10 * value);
	const resolveSonnetSegmentNormalOffset = (role, layoutDirection, rotation, fontSize, randomValue) => {
		if (role !== "support") return {
			x: 0,
			y: 0
		};
		const distance = (Math.min(1, Math.max(0, randomValue)) * 2 - 1) * fontSize * .3;
		const normalAngle = rotation + (layoutDirection === "vertical" ? 0 : Math.PI / 2);
		return {
			x: Math.cos(normalAngle) * distance,
			y: Math.sin(normalAngle) * distance
		};
	};
	const SONNET_CAMERA_SMOOTHING_SAMPLES = [
		{
			offset: -1,
			weight: 1
		},
		{
			offset: -.5,
			weight: 4
		},
		{
			offset: 0,
			weight: 6
		},
		{
			offset: .5,
			weight: 4
		},
		{
			offset: 1,
			weight: 1
		}
	];
	const resolveSonnetSmoothedCameraFocus = (time, startTime, endTime, sampleFocus, smoothingWindow = .12, maxBlendDistance = 96) => {
		const safeStart = Math.min(startTime, endTime);
		const safeEnd = Math.max(startTime, endTime);
		const radius = Math.max(0, smoothingWindow);
		if (radius === 0 || safeStart === safeEnd) return sampleFocus(Math.min(safeEnd, Math.max(safeStart, time)));
		const samples = SONNET_CAMERA_SMOOTHING_SAMPLES.map(({ offset, weight }) => {
			return {
				point: sampleFocus(Math.min(safeEnd, Math.max(safeStart, time + offset * radius))),
				weight
			};
		});
		const center = samples[2].point;
		const maxDistanceSquared = Math.max(0, maxBlendDistance) ** 2;
		let x = 0;
		let y = 0;
		let totalWeight = 0;
		samples.forEach(({ point, weight }) => {
			if ((point.x - center.x) ** 2 + (point.y - center.y) ** 2 > maxDistanceSquared) return;
			x += point.x * weight;
			y += point.y * weight;
			totalWeight += weight;
		});
		return {
			x: x / totalWeight,
			y: y / totalWeight
		};
	};
	const resolveSonnetFocusWeights = (ranges, time, sigma = .35) => {
		if (ranges.length === 0) return [];
		const safeSigma = Math.max(.001, sigma);
		const logWeights = ranges.map((range) => {
			const startTime = Math.min(range.startTime, range.endTime);
			const endTime = Math.max(range.startTime, range.endTime);
			const distance = time < startTime ? startTime - time : time > endTime ? time - endTime : 0;
			return -(distance * distance) / (2 * safeSigma * safeSigma);
		});
		const maxLogWeight = Math.max(...logWeights);
		const weights = logWeights.map((weight) => Math.exp(weight - maxLogWeight));
		const totalWeight = weights.reduce((total, weight) => total + weight, 0);
		return weights.map((weight) => weight / totalWeight);
	};
	const resolveShotPathProgress = (kind, progress) => {
		const linear = clamp01$1(progress);
		if (kind === "tracking-ribbon" || kind === "fragment-collage" || kind === "quiet-tableau" || kind === "poster-blocks") return linear * .55 + easeSonnetInOut(linear) * .45;
		if (linear < .18) return easeSonnetExpoOut(linear / .18) * .22;
		if (linear < .78) return .22 + (linear - .18) / .6 * .56;
		const settle = (linear - .78) / .22;
		return .78 + (1 - (1 - settle) * (1 - settle)) * .22;
	};
	const resolveShotMotionFrame = (kind, progress) => {
		const linear = clamp01$1(progress);
		const eased = resolveShotPathProgress(kind, linear);
		// Only construct the selected frame; the object lookup evaluated all seven
		// variants for every camera sample during whole-song preparation.
		switch (kind) {
			case "editorial-column": return {
				x: -.055 + eased * .095,
				y: .025 - eased * .04,
				scale: .98 + eased * .07,
				rotation: -.006 + eased * .01
			};
			case "type-impact": return {
				x: -.035 + eased * .07,
				y: .018 - eased * .028,
				scale: 1 + (1 - easeSonnetExpoOut(Math.min(linear / .18, 1))) * .22 + eased * .08,
				rotation: -.01 + eased * .016
			};
			case "fragment-collage": return {
				x: -.045 + eased * .085,
				y: .028 - Math.sin(eased * Math.PI) * .055,
				scale: .97 + eased * .09,
				rotation: -.014 + eased * .028
			};
			case "tracking-ribbon": return {
				x: -.16 + eased * .28,
				y: .05 - eased * .085,
				scale: .98 + eased * .07,
				rotation: .008 - eased * .014
			};
			case "mask-reveal": return {
				x: .035 - eased * .065,
				y: .1 - eased * .135,
				scale: .96 + eased * .12,
				rotation: -.006 + eased * .009
			};
			case "poster-blocks": return {
				x: -.012 + eased * .024,
				y: .008 - eased * .016,
				scale: .99 + eased * .025,
				rotation: -.0015 + eased * .003
			};
			case "quiet-tableau": return {
				x: -.022 + eased * .04,
				y: .014 - eased * .025,
				scale: 1 + eased * .028,
				rotation: -.002 + eased * .003
			};
		}
	};
	const SONNET_CAMERA_BREATH_MAX_OFFSET = .006;
	const SONNET_CAMERA_BREATH_MAX_SCALE = .002;
	const SONNET_CAMERA_BREATH_MAX_ROTATION = .0015;
	const resolveSonnetCameraBreath = (time, phase = 0) => {
		const tau = time * Math.PI * 2;
		return {
			x: (Math.sin(tau * .13 + phase) * .65 + Math.sin(tau * .31 + phase * 1.7) * .35) * SONNET_CAMERA_BREATH_MAX_OFFSET,
			y: (Math.cos(tau * .11 + phase * 2.3) * .65 + Math.sin(tau * .29 + phase * .9) * .35) * SONNET_CAMERA_BREATH_MAX_OFFSET,
			scale: Math.sin(tau * .09 + phase * 1.3) * SONNET_CAMERA_BREATH_MAX_SCALE,
			rotation: Math.sin(tau * .07 + phase * 2.9) * SONNET_CAMERA_BREATH_MAX_ROTATION
		};
	};
	const resolveSonnetBreathWeight = (time, revealDoneTime, rampDuration = 1.2) => {
		if (rampDuration <= 0) return time >= revealDoneTime ? 1 : 0;
		return easeSonnetInOut(clamp01$1((time - revealDoneTime) / rampDuration));
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/sonnet/sonnetCameraTracking.ts
	const resolveSonnetSegmentCameraFocus = (glyphs, time, trackingFactor = .5) => {
		if (glyphs.length === 0) return {
			x: 0,
			y: 0
		};
		const first = glyphs[0];
		const last = glyphs[glyphs.length - 1];
		const segCenterX = (first.baseX + last.baseX) / 2;
		const segCenterY = (first.baseY + last.baseY) / 2;
		const applyFactor = (exactX, exactY) => ({
			x: segCenterX + (exactX - segCenterX) * trackingFactor,
			y: segCenterY + (exactY - segCenterY) * trackingFactor
		});
		if (time <= first.startTime) return applyFactor(first.baseX, first.baseY);
		if (time >= last.startTime) return applyFactor(last.baseX, last.baseY);
		for (let index = 0; index < glyphs.length - 1; index += 1) {
			const current = glyphs[index];
			const next = glyphs[index + 1];
			if (time < current.startTime || time > next.startTime) continue;
			const progress = (time - current.startTime) / Math.max(.001, next.startTime - current.startTime);
			return applyFactor(current.baseX + (next.baseX - current.baseX) * progress, current.baseY + (next.baseY - current.baseY) * progress);
		}
		return applyFactor(first.baseX, first.baseY);
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/diorama/cameraPath.ts
/** Distance advanced along the path per lyric line. */
	const DIORAMA_STEP_DISTANCE = 8;
	/** Max heading yaw / pitch (radians) the path bends by. Bounded so the corridor snakes smoothly and
	* always keeps moving forward (never a switchback), which keeps passed lines receding behind you. */
	const DIORAMA_MAX_YAW = .5;
	const DIORAMA_MAX_PITCH = .32;
	/** Per-line lyric text transform bounds (see getDioramaTextPlacement): lateral/vertical offset off the
	* path centerline, in-plane roll, slight yaw, and the off-centre compositional look offset. */
	const DIORAMA_TEXT_OFFSET_R = 1.8;
	const DIORAMA_TEXT_OFFSET_U = 1.2;
	const DIORAMA_TEXT_ROLL = .2;
	const DIORAMA_TEXT_YAW = .16;
	const DIORAMA_TEXT_LOOK = 1.1;
	const clamp01 = (value) => Math.min(1, Math.max(0, value));
	const clamp = (value, min, max) => Math.min(max, Math.max(min, value));
	const vsub = (a, b) => ({
		x: a.x - b.x,
		y: a.y - b.y,
		z: a.z - b.z
	});
	const vcross = (a, b) => ({
		x: a.y * b.z - a.z * b.y,
		y: a.z * b.x - a.x * b.z,
		z: a.x * b.y - a.y * b.x
	});
	const vnorm = (a) => {
		const len = Math.hypot(a.x, a.y, a.z) || 1;
		return {
			x: a.x / len,
			y: a.y / len,
			z: a.z / len
		};
	};
	const seededUnit = (seed) => {
		const x = Math.sin(seed * 12.9898) * 43758.5453;
		return x - Math.floor(x);
	};
	const hashSeed = (seed) => {
		if (typeof seed === "number") return seed;
		if (!seed) return 1;
		let hash = 0;
		for (let i = 0; i < seed.length; i += 1) hash = (hash * 31 + seed.charCodeAt(i)) % 1e5;
		return hash || 1;
	};
	const DIORAMA_SUB_MODE_PRESETS = {
		normal: {
			move: 1,
			drift: .62,
			smooth: .42,
			audio: 1,
			weave: 1
		},
		calm: {
			move: .6,
			drift: .4,
			smooth: .56,
			audio: .5,
			weave: .5
		},
		chaotic: {
			move: 1.35,
			drift: .85,
			smooth: .33,
			audio: 1.35,
			weave: 1.35
		}
	};
	/**
	* Resolve tuning + theme into the motion scale bundle every diorama component consumes. The camera
	* STYLE always follows the theme's animationIntensity - the same single state the player panel's
	* intensity chip cycles and AI themes set - exactly like every other visualizer style. Sliders
	* (cameraSpeed / motionAmount / audioReactivity) multiply on top.
	*/
	const resolveDioramaMotionParams = (tuning, themeIntensity) => {
		const t = tuning ?? DEFAULT_DIORAMA_TUNING;
		const mode = themeIntensity === "calm" ? "calm" : themeIntensity === "chaotic" ? "chaotic" : "normal";
		const preset = DIORAMA_SUB_MODE_PRESETS[mode];
		const motion = clamp(t.motionAmount, .4, 1.6);
		const speed = clamp(t.cameraSpeed, .55, 1.85);
		const audio = clamp(t.audioReactivity, 0, 1.5);
		return {
			moveScale: preset.move * motion,
			driftScale: preset.drift * motion,
			smoothTime: preset.smooth / speed,
			audioLevel: preset.audio * audio,
			weaveScale: preset.weave * motion,
			subMode: mode
		};
	};
	const WORLD_UP = {
		x: 0,
		y: 1,
		z: 0
	};
	/**
	* Build the whole corridor once for a song: a turtle walks forward one step per line while its heading
	* yaws/pitches by bounded seeded oscillations. The heading's forward (-z) component stays negative, so
	* the path always advances. Each line then gets a local frame from the travel tangent (right = travel x
	* world-up so it matches the text's left-to-right reading direction; up completes the frame).
	*/
	const buildDioramaPath = (count, seed) => {
		const base = hashSeed(seed);
		const n = Math.max(count, 1);
		const positions = [];
		let pos = {
			x: 0,
			y: 0,
			z: 0
		};
		for (let i = 0; i <= n; i += 1) {
			positions.push({ ...pos });
			const yaw = DIORAMA_MAX_YAW * (Math.sin(i * .23 + base * .017) * .62 + Math.sin(i * .11 + base * .041 + 1.3) * .38);
			const pitch = DIORAMA_MAX_PITCH * Math.sin(i * .17 + base * .029 + .7);
			const cp = Math.cos(pitch);
			const dir = {
				x: Math.sin(yaw) * cp,
				y: Math.sin(pitch),
				z: -Math.cos(yaw) * cp
			};
			pos = {
				x: pos.x + dir.x * 8,
				y: pos.y + dir.y * 8,
				z: pos.z + dir.z * 8
			};
		}
		const frames = [];
		for (let i = 0; i < n; i += 1) {
			const forward = vnorm(vsub(positions[i + 1], positions[i]));
			const right = vnorm(vcross(forward, WORLD_UP));
			const up = vnorm(vcross(right, forward));
			frames.push({
				position: positions[i],
				forward,
				right,
				up
			});
		}
		return frames;
	};
	/** Compose a local (right, up, depth) offset in a frame into a world point. Positive depth is ahead
	* along the path, away from the trailing camera and behind the text. */
	const composeLocal = (frame, right, up, depth) => ({
		x: frame.position.x + frame.right.x * right + frame.up.x * up + frame.forward.x * depth,
		y: frame.position.y + frame.right.y * right + frame.up.y * up + frame.forward.y * depth,
		z: frame.position.z + frame.right.z * right + frame.up.z * up + frame.forward.z * depth
	});
	/**
	* Where and how a lyric line sits relative to its path frame. Low-frequency sines of the index keep
	* consecutive lines flowing (the weave reads as intentional layout) with a seeded per-line jitter on
	* top; scale/roll/yaw/look are per-line seeded. All variety scales with `weave` (sub-mode/slider) and
	* is deterministic per line/seed. weave=0 collapses to a centered, straight, uniform layout.
	*/
	const getDioramaTextPlacement = (lineIndex, seed, weave = 1) => {
		const base = hashSeed(seed);
		const i = Math.max(lineIndex, 0);
		const jitterR = (seededUnit(base + i * 11 + 2) - .5) * .6;
		const jitterU = (seededUnit(base + i * 11 + 4) - .5) * .6;
		const wanderR = Math.sin(i * .47 + base * .019) * .7 + jitterR;
		const wanderU = Math.sin(i * .29 + base * .027 + .9) * .7 + jitterU;
		return {
			offsetR: wanderR * DIORAMA_TEXT_OFFSET_R * weave,
			offsetU: wanderU * DIORAMA_TEXT_OFFSET_U * weave,
			scale: .82 + seededUnit(base + i * 11 + 5) * .46,
			roll: (seededUnit(base + i * 11 + 6) - .5) * 2 * DIORAMA_TEXT_ROLL * weave,
			yaw: (seededUnit(base + i * 11 + 7) - .5) * 2 * DIORAMA_TEXT_YAW * weave,
			lookR: (seededUnit(base + i * 11 + 8) - .5) * 2 * DIORAMA_TEXT_LOOK * weave
		};
	};
	const SHOT_KINDS = [
		"pushIn",
		"pullBack",
		"orbit",
		"track",
		"crane",
		"hold",
		"swell",
		"spiral",
		"pendulum",
		"flyby",
		"arc",
		"float",
		"glide"
	];
	const CHORUS_PART = /chorus|hook|refrain|drop/i;
	const getShotDir = (lineIndex, seed) => seededUnit(hashSeed(seed) + lineIndex * 7 + 3) < .5 ? -1 : 1;
	const getLineNoteProfile = (line) => {
		let maxNote = 0;
		let sum = 0;
		let noteCount = 0;
		for (const word of line?.words ?? []) {
			const d = word.endTime - word.startTime;
			if (d > 0) {
				maxNote = Math.max(maxNote, d);
				sum += d;
				noteCount += 1;
			}
		}
		return {
			maxNote,
			avgNote: noteCount > 0 ? sum / noteCount : 0,
			noteCount
		};
	};
	const computeShotWeights = (lineIndex, lines, subMode = "normal") => {
		const line = lines[lineIndex];
		const prev = lineIndex > 0 ? lines[lineIndex - 1] : void 0;
		const wordCount = line?.words?.length ?? 0;
		const isChorus = !!line?.isChorus || CHORUS_PART.test(line?.songPart ?? "");
		const sectionStart = !!line && !!prev && (line.blockIndex !== prev.blockIndex || (line.songPart ?? "") !== (prev.songPart ?? ""));
		const { maxNote, avgNote, noteCount } = getLineNoteProfile(line);
		const hasLongNote = maxNote >= 1.3;
		const staccato = noteCount >= 4 && avgNote > 0 && avgNote <= .34;
		const w = {
			pushIn: 1,
			pullBack: 1,
			orbit: 1,
			track: 1,
			crane: 1,
			hold: 1,
			swell: .85,
			spiral: .9,
			pendulum: 1,
			flyby: .9,
			arc: 1,
			float: .9,
			glide: 1
		};
		if (isChorus) {
			w.orbit += 2.4;
			w.pushIn += 1.8;
			w.spiral += 1.2;
			w.crane += .8;
			w.flyby += .6;
			w.arc += 1;
			w.glide += .6;
			w.hold -= .6;
			w.track -= .2;
		} else {
			w.track += 1.1;
			w.crane += .9;
			w.hold += .9;
			w.pendulum += .8;
			w.float += .8;
			w.glide += .6;
			w.arc += .4;
		}
		if (wordCount >= 8) {
			w.track += 1.6;
			w.pullBack += 1;
			w.orbit -= .4;
			w.pushIn -= .3;
		} else if (wordCount <= 3) {
			w.pushIn += 1.4;
			w.orbit += .9;
			w.hold += .5;
			w.track -= .8;
		}
		if (sectionStart) {
			w.pullBack += 2.2;
			w.crane += 1.4;
			w.glide += 1;
			w.pushIn -= .4;
		}
		if (hasLongNote) {
			w.swell += 2.4;
			w.spiral += 1;
			w.float += 1.2;
			w.pendulum += .6;
			w.orbit += .8;
			w.arc += .6;
			w.hold += .3;
			w.track -= .5;
		}
		if (staccato) {
			w.track += 1.4;
			w.flyby += 1.5;
			w.pushIn += .8;
			w.swell -= 1.6;
			w.hold -= .8;
			w.crane -= .4;
		}
		if (subMode === "calm") {
			w.hold += 1.4;
			w.float += 1.3;
			w.swell += .9;
			w.glide += .8;
			w.arc += .5;
			w.crane += .4;
			w.orbit *= .4;
			w.spiral *= .35;
			w.flyby *= .3;
			w.pendulum *= .5;
			w.pushIn *= .7;
		} else if (subMode === "chaotic") {
			w.orbit += 1.3;
			w.spiral += 1.3;
			w.flyby += 1.2;
			w.pendulum += 1;
			w.pushIn += .8;
			w.arc += .6;
			w.crane += .4;
			w.hold *= .35;
			w.float *= .55;
			w.swell *= .7;
		}
		SHOT_KINDS.forEach((key) => {
			w[key] = Math.max(.05, w[key]);
		});
		return w;
	};
	const weightedPickShot = (weights, u) => {
		const total = SHOT_KINDS.reduce((sum, k) => sum + weights[k], 0);
		let acc = clamp01(u) * total;
		for (const k of SHOT_KINDS) {
			acc -= weights[k];
			if (acc <= 0) return k;
		}
		return "hold";
	};
	const basePickShot = (lineIndex, lines, seed, subMode = "normal") => {
		if (!lines[lineIndex]) return "hold";
		const u = seededUnit(hashSeed(seed) + lineIndex * 101 + 7);
		return weightedPickShot(computeShotWeights(lineIndex, lines, subMode), u);
	};
	const getDioramaShot = (lineIndex, lines, seed, subMode = "normal") => {
		const pick = basePickShot(lineIndex, lines, seed, subMode);
		const prev1 = lineIndex > 0 ? basePickShot(lineIndex - 1, lines, seed, subMode) : null;
		const prev2 = lineIndex > 1 ? basePickShot(lineIndex - 2, lines, seed, subMode) : null;
		if (pick === prev1 || pick === prev2) {
			const others = SHOT_KINDS.filter((k) => k !== pick && k !== prev1 && k !== prev2);
			const pool = others.length > 0 ? others : SHOT_KINDS.filter((k) => k !== pick);
			const alt = seededUnit(hashSeed(seed) + lineIndex * 53 + 19);
			return pool[Math.floor(clamp01(alt) * pool.length) % pool.length];
		}
		return pick;
	};
	const DIORAMA_PARTICLE_AUDIO_SCALE_MAX = 1.44;
	const getSpecClearanceRadius = (spec) => {
		const familyRadius = spec.kind === "box" ? .5 : spec.kind === "sphere" ? .74 : spec.kind === "cone" ? .68 : .9;
		return spec.scale * Math.max(familyRadius, spec.stretchY * .55);
	};
	const separateSpecs = (specs) => {
		for (let iter = 0; iter < 6; iter += 1) for (let a = 0; a < specs.length; a += 1) for (let b = a + 1; b < specs.length; b += 1) {
			const A = specs[a];
			const B = specs[b];
			const minDist = (getSpecClearanceRadius(A) + getSpecClearanceRadius(B)) * 1.3;
			let dr = B.r - A.r;
			let du = B.u - A.u;
			let dd = B.d - A.d;
			let dist = Math.hypot(dr, du, dd);
			if (dist >= minDist) continue;
			if (dist < .001) {
				dr = 1;
				du = .5;
				dd = .25;
				dist = Math.hypot(dr, du, dd);
			}
			const push = (minDist - dist) / 2 / dist;
			A.r -= dr * push;
			A.u -= du * push;
			A.d -= dd * push;
			B.r += dr * push;
			B.u += du * push;
			B.d += dd * push;
		}
		specs.forEach((s) => {
			s.d = Math.max(s.d, 1);
		});
	};
	const TEXT_CLEAR_LATERAL = 5.2;
	const TEXT_CLEAR_VERTICAL = 1.4;
	const enforceTextClearance = (specs) => {
		specs.forEach((s, i) => {
			const radius = getSpecClearanceRadius(s) * DIORAMA_PARTICLE_AUDIO_SCALE_MAX;
			if (Math.abs(s.u) - radius < TEXT_CLEAR_VERTICAL && Math.abs(s.r) - radius < TEXT_CLEAR_LATERAL) s.r = (s.r === 0 ? i % 2 === 0 ? 1 : -1 : Math.sign(s.r)) * (TEXT_CLEAR_LATERAL + radius + .35);
		});
	};
	const enforceRailClearance = (specs) => {
		specs.forEach((s, i) => {
			const minimum = 2.8 + getSpecClearanceRadius(s) * DIORAMA_PARTICLE_AUDIO_SCALE_MAX;
			const distance = Math.hypot(s.r, s.u);
			if (distance >= minimum) return;
			const fallbackAngle = (i % 2 === 0 ? 1 : -1) * Math.PI * .16;
			const directionR = distance > .001 ? s.r / distance : Math.cos(fallbackAngle);
			const directionU = distance > .001 ? s.u / distance : Math.sin(fallbackAngle);
			s.r = directionR * minimum;
			s.u = directionU * minimum;
		});
	};
	/**
	* Grow a procedural geometric formation for a line, shaped to match its camera move so the geometry and
	* the shot perform together: a RING for an orbit to sweep around, GATE pillars for a push-in to fly
	* between, a COLONNADE for a track to streak past, an ARCH for a crane to rise under, a HELIX for a
	* spiral to coil past, a hanging CANOPY for a pendulum to swing beneath, a RAIL for a flyby to skim, a
	* TOTEM for a swell, a revealed CLUSTER for a pull-back, a sparse scatter for a hold. Each formation
	* sticks to one primitive family (architectural consistency), uses vertical stretch for pillars/rods,
	* runs overlap separation plus a text clearance belt, and keeps depth >= 0.25 (at/behind the text
	* plane, never between text and camera). Centered on the line's actual text placement offsets.
	*/
	const buildFormation = (lineIndex, seed, shot, frame, placement, volumeScale = 1) => {
		const salt = hashSeed(seed) + lineIndex * 97;
		const rnd = (k) => seededUnit(salt + k);
		const dir = getShotDir(lineIndex, seed);
		const specs = [];
		const safeVolumeScale = Math.min(1.6, Math.max(.65, volumeScale));
		const add = (kind, r, u, d, scale, stretchY = 1, colorSlot) => {
			specs.push({
				kind,
				r,
				u,
				d,
				scale: scale * safeVolumeScale,
				stretchY,
				upright: stretchY > 1.2,
				colorSlot
			});
		};
		const addPair = (kind, lateral, u, d, scale, stretchY = 1, colorSlot = 0) => {
			add(kind, -Math.abs(lateral), u, d, scale, stretchY, colorSlot);
			add(kind, Math.abs(lateral), u, d, scale, stretchY, colorSlot);
		};
		switch (shot) {
			case "orbit": {
				const count = 6;
				const radius = 4.4 + rnd(1) * .6;
				for (let j = 0; j < count; j += 1) {
					const a = j / count * Math.PI * 2 + rnd(2) * .6;
					add(j === 0 ? "torus" : "sphere", Math.cos(a) * radius, Math.sin(a) * radius, .6 + rnd(10 + j) * 1.4, .42 + rnd(20 + j) * .32);
				}
				break;
			}
			case "pushIn": {
				const gate = 4.3 + rnd(1) * .5;
				[-1, 1].forEach((side, idx) => {
					add("box", side * gate, -.6, .5 + rnd(idx) * .5, .55 + rnd(2 + idx) * .2, 2.8 + rnd(6 + idx) * .8);
					add("box", side * (gate + 1.6), .6, 4.5 + rnd(3 + idx) * 2, .7 + rnd(4 + idx) * .3, 3.4);
				});
				break;
			}
			case "track":
				for (let si = 0; si < 2; si += 1) {
					const side = si === 0 ? -1 : 1;
					for (let j = 0; j < 3; j += 1) add("box", side * (4.2 + rnd(si * 5 + j) * .4), -.4 + (rnd(si * 7 + j) - .5) * .6, .5 + j * 2.4, .5 + rnd(si * 3 + j) * .15, 2.6 + rnd(si + j) * .6);
				}
				break;
			case "crane": {
				const count = 5;
				const radius = 3.6 + rnd(1) * .5;
				for (let j = 0; j < count; j += 1) {
					const a = Math.PI * (.15 + .7 * (j / 4));
					add("box", Math.cos(a) * radius, Math.abs(Math.sin(a)) * radius + 1.2, .6 + rnd(30 + j) * .8, .45 + rnd(40 + j) * .25, 1.4);
				}
				break;
			}
			case "swell": {
				const lat = 4.2 + rnd(2) * .5;
				for (let j = 0; j < 2; j += 1) addPair("box", lat, -1.5 + j * 2.1, 1 + j * 1.2, .48 - j * .06, 1.45, j % 2);
				addPair("sphere", lat, 2.65, 3.4, .5, 1, 0);
				break;
			}
			case "spiral": {
				const count = 7;
				const radius = 4.3;
				for (let j = 0; j < count; j += 1) {
					const frac = j / 6;
					const a = dir * frac * Math.PI * 2.2 + rnd(1) * .5;
					add("sphere", Math.cos(a) * radius, -2.2 + frac * 5.2, .5 + frac * 2.2, .32 + rnd(20 + j) * .2);
				}
				break;
			}
			case "pendulum":
				for (let j = 0; j < 4; j += 1) add("box", -3.9 + j * 2.6 + (rnd(j) - .5) * .5, 3 + rnd(j * 2) * 1.2, .5 + rnd(j * 3) * 1.2, .3 + rnd(j * 4) * .12, 2.2 + rnd(j * 5) * .8);
				break;
			case "flyby":
				for (let j = 0; j < 3; j += 1) addPair("box", 4.25 + rnd(j) * .25, -.45 + j * .45, 1 + j * 2.1, .42 + rnd(j * 3) * .1, 2.2, j % 2);
				break;
			case "pullBack":
				addPair("sphere", 5.8 + rnd(9) * .5, 1.25, 6.8 + rnd(11), 1.25 + rnd(12) * .25, 1, 0);
				for (let j = 0; j < 2; j += 1) addPair(j === 0 ? "box" : "cone", 4.4 + j * 1.15, -1.4 + j * 2.2, 2.2 + j * 2.1, .58 + rnd(j * 4) * .18, j === 0 ? 1.5 : 1, 1);
				break;
			case "arc": {
				const count = 5;
				const radius = 5.2 + rnd(1) * .6;
				for (let j = 0; j < count; j += 1) {
					const a = (-.5 + j / 4) * 1.5 * dir;
					add(j === Math.floor(count / 2) ? "torus" : "sphere", Math.sin(a) * radius, .3 + Math.cos(a) * .7, 1 + rnd(20 + j) * 1.6, .45 + rnd(30 + j) * .32);
				}
				break;
			}
			case "float":
				for (let j = 0; j < 3; j += 1) addPair("sphere", 4.1 + j * .85, -1.2 + j * 1.35, 1.2 + j * 1.55, .42 + rnd(j * 4) * .22, 1, j % 2);
				break;
			case "glide":
				for (let j = 0; j < 3; j += 1) addPair("box", 4.25 + rnd(j) * .3, -1.5 + j * 1.7, 1 + j * 1.7, .46 + rnd(j * 2) * .12, 1.85, j % 2);
				break;
			default:
				addPair("torus", 5.6, 1.55, 7.2 + rnd(1), 1.05 + rnd(2) * .2, 1, 0);
				addPair("sphere", 4.45, -1.1, 2.1 + rnd(4), .58 + rnd(5) * .16, 1, 1);
		}
		enforceRailClearance(specs);
		enforceTextClearance(specs);
		separateSpecs(specs);
		enforceRailClearance(specs);
		enforceTextClearance(specs);
		return specs.map((s, j) => ({
			kind: s.kind,
			position: composeLocal(frame, placement.offsetR + s.r, placement.offsetU + s.u, s.d),
			scale: s.scale,
			stretchY: s.stretchY,
			upright: s.upright,
			spinSpeed: .04 + rnd(j * 13 + 5) * .1,
			colorSlot: s.colorSlot ?? j % 2,
			layer: s.d > 3.5 ? "far" : "near"
		}));
	};

//#endregion
//#region ../../web/folia-major/src/components/visualizer/diorama/dioramaParticleSurfaces.ts
	const surfaceCache = /* @__PURE__ */ new Map();
	const TWO_PI = Math.PI * 2;
	const BOX_HALF = .55;
	const CYLINDER_RADIUS = .58;
	const CYLINDER_HALF_HEIGHT = .75;
	const TORUS_MAJOR = .68;
	const TORUS_TUBE = .12;
	const TETRA_VERTICES = [
		[
			0,
			.86,
			0
		],
		[
			-.81,
			-.5,
			.47
		],
		[
			.81,
			-.5,
			.47
		],
		[
			0,
			-.5,
			-.94
		]
	];
	const TETRA_FACES = [
		[
			0,
			1,
			2
		],
		[
			0,
			2,
			3
		],
		[
			0,
			3,
			1
		],
		[
			1,
			3,
			2
		]
	];
	const TETRA_CENTER = [
		0,
		-.16,
		0
	];
	const push = (out, p, n) => {
		out.positions.push(p[0], p[1], p[2]);
		out.normals.push(n[0], n[1], n[2]);
	};
	const unit = (x, y, z) => {
		const length = Math.hypot(x, y, z) || 1;
		return [
			x / length,
			y / length,
			z / length
		];
	};
	/**
	* The outward direction for the flat-faced primitives (box, tetrahedron): straight out from the shape's
	* centre. It is the SMOOTHEST continuous field available, which is what matters - the direction is what
	* the shader displaces along, so how fast it turns between neighbours decides whether the lattice tears.
	* At a face centre it still equals the true face normal; toward an edge it leans over gradually, so a
	* swell inflates the shape elastically instead of sliding each face off on its own vector.
	*
	* A sharper superellipsoid normal (|p/half|^5) was tried to keep the faces flatter, and measurably tore:
	* it crams the whole turn into the last lattice step, leaving points either side of an edge 58 degrees
	* apart - at full swell that is ~2x the lattice spacing of relative motion, i.e. a visible split.
	*/
	const radialNormal = (p, center = [
		0,
		0,
		0
	]) => unit(p[0] - center[0], p[1] - center[1], p[2] - center[2]);
	const countBoxShell = (across, tall) => 2 * (across * tall + tall * across + across * across) - 4 * (across + tall + across) + 8;
	/** The largest shell that fits the budget and stays evenly spaced once the shader applies yStretch. */
	const resolveBoxShell = (budget, stretchY) => {
		let best = [2, 2];
		for (let across = 2; across <= 96; across += 1) {
			const tall = Math.max(2, Math.round(1 + (across - 1) * stretchY));
			if (countBoxShell(across, tall) > budget) break;
			best = [across, tall];
		}
		return best;
	};
	/** One closed shell: every edge and corner point exists exactly once, shared by the faces that meet there. */
	const buildBoxSurface = (budget, stretchY, out) => {
		const [across, tall] = resolveBoxShell(budget, stretchY);
		const axis = (index, steps) => (index / (steps - 1) - .5) * 2 * BOX_HALF;
		out.spacing = 2 * BOX_HALF / Math.max(1, Math.min(across, tall) - 1);
		for (let iy = 0; iy < tall; iy += 1) for (let iz = 0; iz < across; iz += 1) for (let ix = 0; ix < across; ix += 1) {
			if (ix > 0 && ix < across - 1 && iy > 0 && iy < tall - 1 && iz > 0 && iz < across - 1) continue;
			const p = [
				axis(ix, across),
				axis(iy, tall),
				axis(iz, across)
			];
			push(out, p, radialNormal(p));
		}
	};
	/** A ring grid: already welded (the angle wraps) and its radial normal is already continuous. */
	const buildCylinderSurface = (budget, stretchY, out) => {
		const aspect = TWO_PI * CYLINDER_RADIUS / (2 * CYLINDER_HALF_HEIGHT * Math.max(.2, stretchY));
		const rows = Math.max(2, Math.round(Math.sqrt(budget / Math.max(.1, aspect))));
		const columns = Math.max(3, Math.floor(budget / rows));
		out.spacing = Math.max(2 * CYLINDER_HALF_HEIGHT / (rows - 1), TWO_PI * CYLINDER_RADIUS / columns);
		for (let row = 0; row < rows; row += 1) {
			const y = (row / (rows - 1) - .5) * 2 * CYLINDER_HALF_HEIGHT;
			for (let column = 0; column < columns; column += 1) {
				const angle = column / columns * TWO_PI;
				const cosine = Math.cos(angle);
				const sine = Math.sin(angle);
				push(out, [
					cosine * CYLINDER_RADIUS,
					y,
					sine * CYLINDER_RADIUS
				], [
					cosine,
					0,
					sine
				]);
			}
		}
	};
	/**
	* Barycentric lattices that INCLUDE each face's boundary, so the four faces land identical points along
	* their six shared edges. Those coincident points are left in place rather than deduplicated: the shader's
	* displacement is a pure function of position, so they always move as one and simply read as one point.
	*/
	const buildTetrahedronSurface = (budget, out) => {
		let steps = 1;
		while (TETRA_FACES.length * ((steps + 2) * (steps + 3) / 2) <= budget) steps += 1;
		out.spacing = Math.hypot(TETRA_VERTICES[0][0] - TETRA_VERTICES[1][0], TETRA_VERTICES[0][1] - TETRA_VERTICES[1][1], TETRA_VERTICES[0][2] - TETRA_VERTICES[1][2]) / steps;
		for (const [first, second, third] of TETRA_FACES) {
			const a = TETRA_VERTICES[first];
			const b = TETRA_VERTICES[second];
			const c = TETRA_VERTICES[third];
			for (let i = 0; i <= steps; i += 1) for (let j = 0; j <= steps - i; j += 1) {
				const wa = i / steps;
				const wb = j / steps;
				const wc = 1 - wa - wb;
				const p = [
					0,
					1,
					2
				].map((axis) => a[axis] * wa + b[axis] * wb + c[axis] * wc);
				push(out, p, radialNormal(p, TETRA_CENTER));
			}
		}
	};
	/** Already welded (both angles wrap) and its tube normal is already a smooth field. */
	const buildTorusSurface = (budget, out) => {
		const minor = Math.max(4, Math.round(Math.sqrt(budget / 4)));
		const major = Math.max(6, Math.floor(budget / minor));
		out.spacing = Math.max(TWO_PI * .8 / major, TWO_PI * TORUS_TUBE / minor);
		for (let i = 0; i < major; i += 1) {
			const majorAngle = i / major * TWO_PI;
			const majorCosine = Math.cos(majorAngle);
			const majorSine = Math.sin(majorAngle);
			for (let j = 0; j < minor; j += 1) {
				const minorAngle = j / minor * TWO_PI;
				const minorCosine = Math.cos(minorAngle);
				const minorSine = Math.sin(minorAngle);
				const radius = TORUS_MAJOR + TORUS_TUBE * minorCosine;
				push(out, [
					radius * majorCosine,
					TORUS_TUBE * minorSine,
					radius * majorSine
				], [
					minorCosine * majorCosine,
					minorSine,
					minorCosine * majorSine
				]);
			}
		}
	};
	const buildDioramaStructuredSurface = (kind, budget, stretchY = 1) => {
		const stretchKey = Math.round(Math.max(.2, stretchY) * 4) / 4;
		const cacheKey = `${kind}:${budget}:${stretchKey}`;
		const cached = surfaceCache.get(cacheKey);
		if (cached) return cached;
		const out = {
			positions: [],
			normals: [],
			spacing: 0
		};
		if (kind === "box") buildBoxSurface(budget, stretchKey, out);
		else if (kind === "sphere") buildCylinderSurface(budget, stretchKey, out);
		else if (kind === "cone") buildTetrahedronSurface(budget, out);
		else buildTorusSurface(budget, out);
		const positions = Float32Array.from(out.positions);
		const normals = Float32Array.from(out.normals);
		const count = positions.length / 3;
		let total = 0;
		for (let index = 0; index < count; index += 1) total += Math.hypot(positions[index * 3], positions[index * 3 + 1], positions[index * 3 + 2]);
		const radius = count > 0 ? total / count : 1;
		const surface = {
			positions,
			normals,
			radius,
			count,
			spacing: out.spacing / radius
		};
		surfaceCache.set(cacheKey, surface);
		return surface;
	};

//#endregion
//#region .tools/folia-native-layout.ts
	function compilePalette(json) {
		return JSON.stringify(extractRepresentativeColorsFromPixels(JSON.parse(json), 6));
	}
	function compileCadenza(json) {
		const input = JSON.parse(json);
		globalThis.nativeWidths = input.widths;
		globalThis.nativeLines = input.lines;
		const context = {
			showText: true,
			viewport: {
				width: input.width,
				height: input.height
			},
			theme: {
				fontStyle: "sans",
				fontFamily: input.fontFamily,
				fontWeight: input.fontWeight,
				animationIntensity: "normal",
				primaryColor: "#b8d6cc",
				accentColor: "#c47a9c"
			},
			tuning: {
				...DEFAULT_CADENZA_TUNING,
				fontScale: DEFAULT_CADENZA_TUNING.fontScale * input.fontScale
			}
		};
		return JSON.stringify(input.lines.map((line) => {
			const p = buildPreparedState(line, context);
			return {
				font: p.fontPx,
				placements: p.placements
			};
		}));
	}
	function compilePendolo(json) {
		const input = JSON.parse(json), radius = Math.min(input.width, input.height) * .42;
		const scene = buildPendoloClockworkCachedScene({
			centerX: 0,
			centerY: input.height * .5,
			baseRadius: radius,
			lyricRingRadius: radius / .42 * .48,
			escapementAngle: 0,
			phase: 0,
			bassOscillation: 0,
			secondGearAngle: 0,
			primaryTextColor: input.theme?.primaryColor ?? "#b8d6cc",
			accentTextColor: input.theme?.accentColor ?? "#c47a9c",
			backgroundColor: input.theme?.backgroundColor ?? "#0e1118",
			showGearDecor: "subtle",
			showCenterGradient: true,
			showCover: false,
			coverImage: null
		});
		const zero = {
			escapementAngle: 0,
			phase: 0,
			bassOscillation: 0,
			secondGearAngle: 0
		};
		const parts = scene.parts.map((p) => {
			const origin = p.getOrigin(zero), other = p.getOrigin({
				...zero,
				escapementAngle: 1
			}), base = p.getRotation(zero);
			const orbit = Math.hypot(other.x - origin.x, other.y - origin.y) > 1e-5;
			const angle = Math.atan2(origin.y - input.height * .5, origin.x);
			let speed = Math.atan2(other.y - input.height * .5, other.x) - angle;
			if (speed > Math.PI) speed -= Math.PI * 2;
			if (speed < -Math.PI) speed += Math.PI * 2;
			return {
				origin: [origin.x, origin.y],
				orbit: orbit ? [
					Math.hypot(origin.x, origin.y - input.height * .5),
					angle,
					speed
				] : null,
				base,
				coefficients: [
					"escapementAngle",
					"phase",
					"bassOscillation",
					"secondGearAngle"
				].map((k) => p.getRotation({
					...zero,
					[k]: 1
				}) - base)
			};
		});
		return JSON.stringify({
			data: Array.from(scene.data),
			parts
		});
	}
	function compileTilt(json) {
		const input = JSON.parse(json);
		globalThis.nativeWidths = input.widths;
		globalThis.window = { innerWidth: input.width };
		const theme = {
			fontStyle: "sans",
			fontFamily: input.fontFamily,
			fontWeight: input.fontWeight
		};
		return JSON.stringify(input.lines.map((line) => buildTiltLayout(line.fullText, line.startTime, {
			splitProbability: .75,
			tiltStyleProbability: .35
		}, theme, input.fontScale)));
	}
	function compileFume(json) {
		const input = JSON.parse(json);
		globalThis.nativeWidths = input.widths;
		globalThis.nativeLines = input.lines;
		const viewport = {
			width: input.width,
			height: input.height
		}, theme = {
			name: "Spectrum reference",
			fontStyle: "sans",
			fontFamily: input.fontFamily,
			fontWeight: input.fontWeight
		};
		const article = buildArticleLayout(input.lines, viewport, theme, input.fontScale, { heroScale: 1 });
		if (!article) return JSON.stringify({
			blocks: [],
			camera: []
		});
		const end = Math.max(...input.lines.map((l) => l.endTime)) + 4, camera = [];
		let lineIndex = -1;
		for (let i = 0; i <= Math.ceil(end * 20); i++) {
			const time = i / 20;
			while (lineIndex + 1 < input.lines.length && input.lines[lineIndex + 1].startTime <= time) lineIndex++;
			const block = resolveFocusBlock(article, lineIndex, time);
			const point = resolveSmoothBlockFocusPoint(block, resolvePrintedGraphemeProgress(block.line, block.wordRanges, block.graphemes.length, time));
			camera.push([
				point.x,
				point.y,
				resolveCameraScaleForBlock(block, viewport)
			]);
		}
		return JSON.stringify({
			width: article.width,
			height: article.height,
			background: buildFumeBackgroundScene({
				viewport,
				world: {
					width: article.width,
					height: article.height
				},
				paperBounds: article.paperBounds,
				seed: input.seed + ":" + theme.name
			}),
			camera,
			blocks: article.blocks.map((b) => ({
				index: b.sourceLineIndex,
				x: b.x,
				y: b.y,
				width: b.width,
				height: b.height,
				font: b.fontPx,
				lineHeight: b.lineHeight,
				hero: b.variant === "hero",
				rows: b.renderLines.map((l) => ({
					start: l.start,
					end: l.end,
					left: l.left,
					top: l.top,
					offsets: l.glyphOffsets
				}))
			}))
		});
	}
	const color = (value) => {
		if (typeof value === "number") return value;
		if (value.startsWith("#")) return parseInt(value.slice(1, 7), 16);
		const n = value.match(/[\d.]+/g)?.map(Number) ?? [
			255,
			255,
			255
		];
		return Math.round(n[0]) * 65536 + Math.round(n[1]) * 256 + Math.round(n[2]);
	};
	var Container = class {
		constructor() {
			this.x = 0;
			this.y = 0;
			this.rotation = 0;
			this.sx = 1;
			this.sy = 1;
			this.px = 0;
			this.py = 0;
			this.parent = null;
			this.children = [];
			this.position = { set: (x, y = x) => {
				this.x = x;
				this.y = y;
			} };
			this.pivot = { set: (x, y = x) => {
				this.px = x;
				this.py = y;
			} };
			this.scale = { set: (x, y = x) => {
				this.sx = x;
				this.sy = y;
			} };
		}
		addChild(n) {
			n.parent = this;
			this.children.push(n);
			return n;
		}
	};
	var Graphics = class extends Container {
		constructor() {
			super();
			this.pending = [];
			this.events = [];
			this.last = [];
		}
		poly(points) {
			this.pending.push({
				points: [...points],
				closed: true
			});
			return this;
		}
		rect(x, y, w, h) {
			return this.poly([
				x,
				y,
				x + w,
				y,
				x + w,
				y + h,
				x,
				y + h
			]);
		}
		circle(x, y, r) {
			const pts = [];
			for (let i = 0; i < 64; i++) {
				let a = i / 64 * Math.PI * 2;
				pts.push(x + Math.cos(a) * r, y + Math.sin(a) * r);
			}
			return this.poly(pts);
		}
		moveTo(x, y) {
			this.pending.push({
				points: [x, y],
				closed: false
			});
			return this;
		}
		lineTo(x, y) {
			this.pending.at(-1).points.push(x, y);
			return this;
		}
		fill(style) {
			this.last = this.pending.length ? this.pending : this.last;
			this.events.push({
				paths: this.last.map((p) => ({
					...p,
					points: [...p.points]
				})),
				style,
				stroke: false,
				holes: []
			});
			this.pending = [];
			return this;
		}
		stroke(style) {
			this.last = this.pending.length ? this.pending : this.last;
			this.events.push({
				paths: this.last.map((p) => ({
					...p,
					points: [...p.points]
				})),
				style,
				stroke: true,
				holes: []
			});
			this.pending = [];
			return this;
		}
		cut() {
			this.events.at(-1).holes.push(...this.pending);
			this.pending = [];
			return this;
		}
		destroy() {}
	};
	const pixi = {
		Graphics,
		Container,
		Color: { shared: {
			setValue(value) {
				this.value = color(value);
				return this;
			},
			toNumber() {
				return this.value;
			}
		} }
	};
	const transform = (node, x, y) => {
		let current = node;
		while (current) {
			const px = (x - current.px) * current.sx, py = (y - current.py) * current.sy, c = Math.cos(current.rotation), s = Math.sin(current.rotation);
			x = px * c - py * s + current.x;
			y = px * s + py * c + current.y;
			current = current.parent;
		}
		return [x, y];
	};
	function compileTempera(json) {
		const input = JSON.parse(json);
		globalThis.nativeWidths = input.widths;
		globalThis.nativeLines = input.lines;
		const program = compileTemperaProgram(input.lines, input.seed);
		const palette = resolveTemperaPalette(input.theme ?? {
			backgroundColor: "#0e1118",
			primaryColor: "#b8d6cc",
			secondaryColor: "#4f6982",
			accentColor: "#c47a9c"
		}, { colorMode: "duo" }), shots = [];
		for (const paragraph of program.paragraphs) for (let si = 0; si < paragraph.shots.length; si++) {
			const shot = paragraph.shots[si], seed = hashVisualizerSeed(input.seed + ":" + paragraph.id) + si * 97;
			const width = input.width, height = input.height, stage = new Container(), nodes = [];
			const ctx = {
				pixi,
				kind: shot.kind,
				palette,
				decor: shot.decor,
				width,
				height,
				seed,
				showDecor: input.temperaDecor !== false,
				bleed: Math.max(width, height) * .17,
				flowAngle: shot.flowAngle,
				gradient: null,
				add(node, options = {}, parent) {
					(parent ?? stage).addChild(node);
					nodes.push({
						node,
						options
					});
				},
				createGroup(rotation, x, y) {
					const g = new Container();
					g.rotation = rotation;
					g.position.set(x, y);
					stage.addChild(g);
					return g;
				}
			};
			drawTemperaComposition(ctx);
			const graphics = [];
			for (let ni = 0; ni < nodes.length; ni++) {
				const { node, options } = nodes[ni], primitives = [];
				for (const event of node.events) {
					const rgb = color(event.style.color ?? palette.ink), alpha = event.style.alpha ?? 1;
					if (event.stroke) for (const path of event.paths) {
						const n = path.points.length / 2;
						for (let i = 1; i < n + (path.closed ? 1 : 0); i++) primitives.push([
							1,
							rgb,
							alpha,
							event.style.width ?? 1,
							...transform(node, path.points[(i - 1) % n * 2], path.points[(i - 1) % n * 2 + 1]),
							...transform(node, path.points[i % n * 2], path.points[i % n * 2 + 1]),
							0,
							0
						]);
					}
					else for (const polygon of event.paths) {
						if (polygon.points.length < 6) continue;
						const points = [...polygon.points], holes = [];
						for (const hole of event.holes) {
							holes.push(points.length / 2);
							points.push(...hole.points);
						}
						const triangles = earcut(points, holes, 2);
						for (let i = 0; i < triangles.length; i += 3) primitives.push([
							2,
							rgb,
							alpha,
							0,
							...triangles.slice(i, i + 3).flatMap((index) => transform(node, points[index * 2], points[index * 2 + 1]))
						]);
					}
				}
				let rotation = 0;
				for (let n = node.parent; n; n = n.parent) rotation += n.rotation;
				graphics.push({
					options,
					center: transform(node, node.px, node.py),
					rotation,
					phase: visualizerHash01(seed, ni + 1, 173) * Math.PI * 2,
					primitives
				});
			}
			const groups = shot.slices.map((slice) => paragraph.lines.find((l) => l.sourceIndex === slice.lineIndex)?.segments.slice(slice.segmentStart, slice.segmentEnd).filter((s) => s.text.trim().length) ?? []).filter((a) => a.length);
			const max = Math.max(3, ...groups.map((segments) => segments.reduce((s, g) => s + g.graphemes.length, 0)));
			const base = Math.max(34, Math.min(150, width / Math.max(5, max * 1.05) * 1.5)) * input.fontScale;
			const glyphs = resolveTemperaLayout({
				lines: groups,
				shotKind: shot.kind,
				width,
				height,
				baseFontSize: base,
				fontFamily: input.fontFamily,
				fontWeight: input.fontWeight,
				seed,
				settleStretch: .5
			});
			shots.push({
				...shot,
				nodes: graphics,
				glyphs,
				postSeed: hashVisualizerSeed(input.seed + ":" + paragraph.id) % 1e4 / 1e4
				,paragraph: program.paragraphs.indexOf(paragraph), paragraphStart: paragraph.startTime,
				transitionOut: paragraph.transitionOut,
				transitionIn: program.paragraphs[program.paragraphs.indexOf(paragraph) - 1]?.transitionOut ?? null
			});
		}
		return JSON.stringify(shots);
	}
	function compileDiorama(json) {
		const input = JSON.parse(json), motion = resolveDioramaMotionParams(void 0, "normal");
		const path = buildDioramaPath(input.lines.length, input.seed), surfaces = [], cache = /* @__PURE__ */ new Map();
		const lines = input.lines.map((line, i) => {
			const frame = path[i], placement = getDioramaTextPlacement(i, input.seed, motion.weaveScale), kind = getDioramaShot(i, input.lines, input.seed, motion.subMode);
			return {
				frame,
				placement,
				kind,
				clusters: buildFormation(i, input.seed, kind, frame, placement, 1).map((cluster) => {
					const key = cluster.kind + ":" + cluster.stretchY;
					if (!cache.has(key)) {
						const surface = buildDioramaStructuredSurface(cluster.kind, 576, cluster.stretchY);
						cache.set(key, surfaces.length);
						surfaces.push({
							radius: surface.radius,
							points: Array.from(surface.positions)
						});
					}
					return {
						...cluster,
						surface: cache.get(key)
					};
				})
			};
		});
		return JSON.stringify({
			lines,
			surfaces
		});
	}
	function compile(json, raw = false) {
		const input = JSON.parse(json);
		globalThis.nativeWidths = input.widths;
		const program = compileSonnetProgram(input.lines, input.seed);
		const shots = [];
		const programLines = program.paragraphs.flatMap((p) => p.lines);
		for (const paragraph of program.paragraphs) for (const shot of paragraph.shots) {
			const groups = shot.lineIndices.map((i) => programLines.find((l) => l.sourceIndex === i)).map((l) => l.segments.filter((s) => s.text.trim().length));
			const segments = groups.flat();
			if (!segments.length) continue;
			const count = Math.max(1, segments.filter((s) => s.isWordLike).length);
			const baseFont = Math.max(24, Math.min(112, input.width / Math.max(7, count * 2.15) * (shot.kind === "type-impact" ? 1.55 : shot.kind === "quiet-tableau" ? .82 : 1) * input.fontScale));
			const texts = resolveSonnetTypographyLayout({
				lines: groups,
				shotKind: shot.kind,
				paragraphKind: paragraph.kind,
				width: input.width,
				height: input.height,
				baseFontSize: baseFont,
				fontFamily: input.fontFamily,
				fontWeight: input.fontWeight
			}).map((p, pi) => {
				const s = segments[p.segmentIndex], font = baseFont * p.fontScale;
				const normal = resolveSonnetSegmentNormalOffset(p.role, p.layoutDirection, p.rotation, font, hashVisualizerSeed([
					s.text,
					s.startOffset,
					s.endOffset,
					pi,
					"normal-offset"
				].join(":")) / 4294967295);
				p.x += normal.x;
				p.y += normal.y;
				const glyphs = buildSonnetGlyphLayout(s, p, font, (char) => (input.widths[char] ?? .6) * font, {
					startTime: shot.startTime,
					endTime: shot.endTime
				});
				return {
					role: p.role,
					font,
					segment: s,
					placement: p,
					segmentIndex: pi,
					isChorus: paragraph.kind === "chorus",
					rotation: p.rotation,
					vertical: p.vertical,
					glyphs: glyphs.map((g, i) => ({
						...g,
						line: s.graphemes[i]?.line,
						index: s.graphemes[i]?.index
					}))
				};
			});
			const tracked = texts.filter((t) => t.role !== "decoration" && t.glyphs.length);
			const ranges = tracked.map((t) => ({
				startTime: t.glyphs[0].startTime,
				endTime: t.glyphs.at(-1).startTime
			}));
			const focusAt = (time) => {
				const weights = resolveSonnetFocusWeights(ranges, time);
				let x = 0, y = 0;
				tracked.forEach((s, i) => {
					const f = resolveSonnetSegmentCameraFocus(s.glyphs, time);
					x += f.x * weights[i];
					y += f.y * weights[i];
				});
				return {
					x,
					y
				};
			};
			const duration = Math.max(.001, shot.endTime - shot.startTime);
			const done = Math.max(...tracked.map((t) => t.glyphs.at(-1)?.startTime ?? shot.endTime));
			const breathPhase = hashVisualizerSeed(shot.id) % 1024 / 1024 * Math.PI * 2;
			const nativeCamera = typeof nativeSonnetCamera === "function" ? nativeSonnetCamera : null;
			const frames = nativeCamera ? nativeCamera(shot.kind, shot.startTime, shot.endTime, breathPhase, tracked) : [];
			for (let i = 0; !nativeCamera && i <= 240; i++) {
				const progress = i / 240, time = shot.startTime + duration * progress, frame = resolveShotMotionFrame(shot.kind, progress);
				const focus = resolveSonnetSmoothedCameraFocus(time, shot.startTime, shot.endTime, focusAt);
				const breathWeight = resolveSonnetBreathWeight(time, done), breath = resolveSonnetCameraBreath(time, breathPhase);
				frame.x += breath.x * breathWeight;
				frame.y += breath.y * breathWeight;
				frame.scale += breath.scale * breathWeight;
				frame.rotation += breath.rotation * breathWeight;
				frames.push([
					focus.x,
					focus.y,
					frame.x,
					frame.y,
					frame.scale,
					frame.rotation
				]);
			}
			shots.push({
				kind: shot.kind,
				start: shot.startTime,
				end: shot.endTime,
				camera: shot.camera,
				texts,
				frames,
				seed: hashVisualizerSeed(`${program.seed}:${paragraph.id}`) + paragraph.shots.indexOf(shot) * 97,
				ordinal: paragraph.shots.indexOf(shot),
				cues: shot.cues.map(c => c.at),
				paragraph: program.paragraphs.indexOf(paragraph),
				paragraphStart: paragraph.startTime,
				transitionOut: paragraph.transitionOut,
				transitionIn: program.paragraphs[program.paragraphs.indexOf(paragraph) - 1]?.transitionOut ?? null,
				transitionSeed: hashVisualizerSeed(`${program.seed}:${paragraph.id}:transition-frame`)
			});
		}
		return raw ? shots : JSON.stringify(shots);
	}

//#endregion
exports.compile = compile;
// Reuses the already-vendored Earcut implementation at the native geometry preparation boundary.
exports.triangulateSonnet = earcut;
exports.compileCadenza = compileCadenza;
// Native Classic adapter: keep original semantic/sticky display planning on the cold path.
exports.compileClassic = function(json) {
    const input = JSON.parse(json);
    globalThis.nativeWidths = input.widths;
    globalThis.nativeLines = input.lines;
    return JSON.stringify(input.lines.map(line => buildDisplayWordsFromLayoutUnits(
        buildPostLyricLayoutUnits(line, { semantic: true, sticky: true }))));
};
exports.compileDiorama = compileDiorama;
exports.compileFume = compileFume;
exports.compileLumiere = compileLumiere;
exports.compilePalette = compilePalette;
exports.compilePartita = compilePartita;
exports.compilePendolo = compilePendolo;
exports.compileTempera = compileTempera;
exports.compileTilt = compileTilt;
return exports;
})({});
