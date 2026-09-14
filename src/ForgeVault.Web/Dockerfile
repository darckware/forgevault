# --- build stage ---
FROM node:20-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
# Public reCAPTCHA v2 site key (not a secret -- it's meant to ship in frontend JS) -- baked
# in at build time since Vite only ever reads import.meta.env.VITE_* at build, never at
# runtime. Empty by default: LoginPage.tsx only renders the widget when this is set, same
# convention as ForgeHub's frontend/Dockerfile, so build/login keep working unchanged until
# a key pair is provisioned for this domain.
ARG VITE_RECAPTCHA_SITE_KEY=""
ENV VITE_RECAPTCHA_SITE_KEY=$VITE_RECAPTCHA_SITE_KEY
RUN npm run build

# --- serve stage ---
FROM nginx:1.27-alpine
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
