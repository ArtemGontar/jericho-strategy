FROM node:22-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM node:22-alpine
WORKDIR /app
ENV NODE_ENV=production PORT=8080 DATA_DIR=/data
COPY package*.json ./
RUN npm ci
COPY --from=build /app/dist ./dist
COPY src ./src
COPY tsconfig.json ./
VOLUME /data
EXPOSE 8080
CMD ["npx", "tsx", "src/server/index.ts"]
