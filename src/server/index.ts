import { startServer } from './server';

const { port } = await startServer({});
console.log(`JERICHO server listening on http://localhost:${port}`);
console.log('Share this address (or a tunnel / deployed URL) with your friends and pick "Play online".');
