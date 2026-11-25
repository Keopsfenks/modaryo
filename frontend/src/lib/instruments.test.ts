import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";
import { allowedTransitions, INSTRUMENT_TRANSITIONS } from "./instruments";

const camel = (s: string) => s.charAt(0).toLowerCase() + s.slice(1);

describe("instruments", () => {
    it("Transitions_MatchServerTable", () => {
        const source = readFileSync(resolve(process.cwd(), "../services/erp/src/Domain/Treasury/InstrumentStateMachine.cs"), "utf-8");
        const server = [...source.matchAll(/InstrumentDirection\.(\w+), InstrumentStatus\.(\w+), InstrumentStatus\.(\w+)/g)].map((m) => [camel(m[1]), camel(m[2]), camel(m[3])].join(">"));
        const client = INSTRUMENT_TRANSITIONS.map((t) => t.join(">"));

        expect(server.length).toBeGreaterThan(0);
        expect([...client].sort()).toEqual([...server].sort());
    });

    it("AllowedTransitions_FollowDirectionAndStatus", () => {
        expect(allowedTransitions("received", "inPortfolio")).toEqual(["endorsed", "inCollection", "returnedToCustomer"]);
        expect(allowedTransitions("issued", "issued")).toEqual(["paid", "returnedFromSupplier"]);
        expect(allowedTransitions("received", "collected")).toEqual([]);
        expect(allowedTransitions("issued", "inPortfolio")).toEqual([]);
    });
});
