import { setupServer } from 'msw/node'

// No default handlers: each test declares exactly the requests it expects.
export const server = setupServer()
