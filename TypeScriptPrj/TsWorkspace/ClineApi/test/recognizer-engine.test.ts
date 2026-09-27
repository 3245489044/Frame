/**
 * RecognizerEngine / 声明面 / 词表 —— K1 判据的机器化载体。
 *
 * 覆盖：D-1（声明驱动，零代码新增类别）· D-3（识别账平衡，无静默丢）· D-5（未知 kind 容错）
 *      · W-3（非法声明装配期拒）· W-4（缺失即抛 / ∅+诊断）。
 */

import { describe, it, expect } from "vitest";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { fileURLToPath } from "node:url";
import { RecognizerEngine, type RecognizerFrame } from "../src/recognize/recognizer-engine.js";
import { loadRecognizerSpec } from "../src/recognize/recognizer-spec-loader.js";
import {
  ensureCompatible,
  normalizeKind,
  RecognizerVocabularyError,
  type RecognizerSpec,
} from "../src/recognize/vocabulary.js";

const HERE = path.dirname(fileURLToPath(import.meta.url));
/** 声明面真源（决策 A：JSON 过渡）—— 跨 Frame/Projects，按仓库布局上溯定位。 */
const SPEC_PATH = path.resolve(
  HERE, "..", "..", "..", "..", "..", "Projects", "Plug", "Agent", "Config", "recognizers.json"
);

function realSpec(): RecognizerSpec {
  const spec = loadRecognizerSpec(SPEC_PATH);
  if (!spec) throw new Error("声明面未加载（路径错误）");
  return spec;
}

function frame(source: string, eventType: string, payload: Record<string, unknown> = {}): RecognizerFrame {
  return { source, eventType, payload };
}

/** K0 实测序列（带工具调用那次真实 run 的直方图展开，共 20 帧）。 */
function k0MeasuredSequence(): RecognizerFrame[] {
  const seq: RecognizerFrame[] = [];
  const push = (n: number, f: () => RecognizerFrame) => {
    for (let i = 0; i < n; i++) seq.push(f());
  };
  push(2, () => frame("agent_event", "iteration_start", { iteration: 1 }));
  push(4, () => frame("agent_event", "content_start", { contentType: "reasoning" }));
  push(2, () => frame("agent_event", "usage", { inputTokens: 10, outputTokens: 2 }));
  seq.push(frame("agent_event", "content_end", { contentType: "reasoning", reasoning: "think" }));
  seq.push(
    frame("agent_event", "content_start", {
      contentType: "tool", toolName: "read_files", toolCallId: "t1", input: { p: 1 },
    })
  );
  seq.push(
    frame("agent_event", "content_end", {
      contentType: "tool", toolName: "read_files", toolCallId: "t1", output: "ok", durationMs: 5,
    })
  );
  push(2, () => frame("agent_event", "iteration_end", { iteration: 1, toolCallCount: 1, hadToolCalls: true }));
  push(5, () => frame("agent_event", "content_start", { contentType: "text" }));
  seq.push(frame("agent_event", "content_end", { contentType: "text", text: "ok" }));
  seq.push(frame("agent_event", "done", { reason: "completed", iterations: 2, text: "ok" }));
  return seq;
}

describe("K1 · 声明面装载（W-4 缺失即抛 / ∅+诊断）", () => {
  it("未配置路径 ⇒ undefined（识别层停用，不假装生效）", () => {
    expect(loadRecognizerSpec(undefined)).toBeUndefined();
    expect(loadRecognizerSpec("")).toBeUndefined();
  });

  it("配置了但文件缺失 ⇒ 抛（禁回退默认词表）", () => {
    expect(() => loadRecognizerSpec(path.join(HERE, "no-such-recognizers.json"))).toThrow(/识别声明缺失/);
  });

  it("版本不兼容 ⇒ 抛", () => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), "rec-"));
    const file = path.join(dir, "r.json");
    fs.writeFileSync(file, JSON.stringify({ vocabulary: "cline-signal@99", kinds: [], recognizers: [] }));
    expect(() => loadRecognizerSpec(file)).toThrow(RecognizerVocabularyError);
  });

  it("真源声明可装载，且覆盖 K0 实测到达的类别", () => {
    const spec = realSpec();
    ensureCompatible(spec.vocabulary);
    for (const kind of ["llm-iteration", "llm-text", "llm-reasoning", "tool-call", "tool-result", "usage", "done"]) {
      expect(spec.kinds).toContain(kind);
    }
    expect(spec.kinds).toContain("unrecognized");
  });
});

describe("K1 · 识别账（D-3 无静默丢）", () => {
  it("K0 实测 20 帧 ⇒ emitted + countedOnly + unrecognized = total，且零未识别", () => {
    const engine = new RecognizerEngine(realSpec());
    const produced = k0MeasuredSequence().flatMap((f) => engine.apply(f));

    const stats = engine.stats();
    expect(stats.total).toBe(20);
    expect(stats.emitted).toBe(11);
    expect(stats.countedOnly).toBe(9); // content_start 的 text/reasoning 为流式片段：识别但不成节点
    expect(stats.unrecognized).toBe(0);
    expect(stats.emitted + stats.countedOnly + stats.unrecognized).toBe(stats.total);
    expect(produced.filter((n) => n.node)).toHaveLength(11);
    expect(stats.byKind["tool-call"]).toBe(1);
    expect(stats.byKind["tool-result"]).toBe(1);
    expect(stats.byKind["llm-iteration"]).toBe(4);
  });

  it("无匹配帧 ⇒ unrecognized 计数 + 按 source/eventType 分桶（禁静默丢）", () => {
    const engine = new RecognizerEngine(realSpec());
    engine.apply(frame("unknown_source", "mystery", { text: "x" }));
    engine.apply(frame("unknown_source", "mystery", { text: "y" }));
    engine.apply(frame("hook", "session_shutdown")); // hook 规则未覆盖 ⇒ 落未识别

    const stats = engine.stats();
    expect(stats.unrecognized).toBe(3);
    expect(stats.unrecognizedBuckets).toEqual({ "unknown_source/mystery": 2, "hook/session_shutdown": 1 });
    expect(stats.emitted).toBe(0);
  });
});


describe("K1 · D-1 声明驱动性（新增一类零代码）", () => {
  it("声明 +1 项 + 词表 +1 项 ⇒ 引擎产出该类别（引擎代码未变）", () => {
    const base = realSpec();
    const patched: RecognizerSpec = {
      vocabulary: base.vocabulary,
      kinds: [...base.kinds, "probe-fake"],
      recognizers: [
        ...base.recognizers,
        { match: { source: "agent_event", type: "probe_fake" }, kind: "probe-fake", payload: { n: "n" } },
      ],
    };

    const engine = new RecognizerEngine(patched);
    const out = engine.apply(frame("agent_event", "probe_fake", { n: 7 })).filter((n) => n.node);

    expect(out).toHaveLength(1);
    expect(out[0].kind).toBe("probe-fake");
    expect(out[0].payload).toEqual({ n: 7 });
  });

  it("ordinal 为该类别内到达序（1-based）", () => {
    const engine = new RecognizerEngine(realSpec());
    const a = engine.apply(frame("agent_event", "iteration_end", { iteration: 1 }))[0];
    const b = engine.apply(frame("agent_event", "iteration_end", { iteration: 2 }))[0];
    expect([a.ordinal, b.ordinal]).toEqual([1, 2]);
  });
});

describe("K1 · 结构谓词与载荷白名单", () => {
  const spec: RecognizerSpec = {
    vocabulary: "cline-signal@1",
    kinds: ["probe-a", "unrecognized"],
    recognizers: [
      { match: { type: ["x", "y"] }, kind: "probe-a" },
      { match: { type: "z", output: { exists: true } }, kind: "probe-a", payload: { out: "output" } },
    ],
  };

  it("∈集合谓词命中", () => {
    const engine = new RecognizerEngine(spec);
    expect(engine.apply(frame("agent_event", "y")).filter((n) => n.node)).toHaveLength(1);
    expect(engine.apply(frame("agent_event", "nope")).filter((n) => n.node)).toHaveLength(0);
  });

  it("存在性谓词：字段缺失即不匹配（禁语义兜底）", () => {
    const engine = new RecognizerEngine(spec);
    expect(engine.apply(frame("agent_event", "z")).filter((n) => n.node)).toHaveLength(0);
    expect(engine.apply(frame("agent_event", "z", { output: 1 })).filter((n) => n.node)).toHaveLength(1);
  });

  it("载荷仅白名单投影（未列字段不外泄）", () => {
    const engine = new RecognizerEngine(realSpec());
    const out = engine
      .apply(
        frame("agent_event", "content_end", {
          contentType: "tool", toolName: "t", toolCallId: "c", output: "o", secret: "S",
        })
      )
      .filter((n) => n.node);
    expect(out[0].payload).toEqual({ tool: "t", callId: "c", output: "o" });
    expect(JSON.stringify(out[0].payload)).not.toContain("S");
  });

  it("error 事件的对象字段不进入载荷（仅白名单标量）", () => {
    const engine = new RecognizerEngine(realSpec());
    const out = engine
      .apply(
        frame("agent_event", "error", {
          error: new Error("boom"), errorClass: "provider", recoverable: false, iteration: 3,
        })
      )
      .filter((n) => n.node);
    expect(out[0].payload).toEqual({ errorClass: "provider", recoverable: false, iteration: 3 });
  });
});

describe("K1 · 装配期拒非法声明（W-3）与 D-5 容错", () => {
  it("规则 kind ∉ 词表 ⇒ 构造即抛", () => {
    expect(
      () =>
        new RecognizerEngine({
          vocabulary: "cline-signal@1",
          kinds: ["unrecognized"],
          recognizers: [{ match: { type: "a" }, kind: "ghost" }],
        })
    ).toThrow(/∉ 词表/);
  });

  it("词表缺兜底类 ⇒ 构造即抛", () => {
    expect(
      () => new RecognizerEngine({ vocabulary: "cline-signal@1", kinds: ["a"], recognizers: [] })
    ).toThrow(/兜底类/);
  });

  it("未知/缺失 kind 归一为 unrecognized（跨语言容错，不崩）", () => {
    const allowed = new Set(["a", "unrecognized"]);
    expect(normalizeKind("ghost", allowed)).toBe("unrecognized");
    expect(normalizeKind(undefined, allowed)).toBe("unrecognized");
    expect(normalizeKind("a", allowed)).toBe("a");
  });
});
