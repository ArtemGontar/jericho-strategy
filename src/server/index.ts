import { startServer } from './server';

const { port } = await startServer({});
console.log(`JERICHO server listening on http://localhost:${port}`);
console.log('Share this address (or a tunnel / deployed URL) with your friends and pick "Play online".');

// last line of defence: log and keep serving rather than dropping every running game on one stray exception
process.on('uncaughtException', (e) => console.error('uncaught exception', e));
process.on('unhandledRejection', (e) => console.error('unhandled rejection', e));
