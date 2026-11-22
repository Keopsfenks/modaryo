import { setupServer } from "msw/node";

/** Testlerin sahte API sunucusu. Her test kendi yanıtlarını server.use(...) ile ekler. */
export const server = setupServer();
