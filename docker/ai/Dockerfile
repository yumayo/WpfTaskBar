FROM ubuntu

RUN apt-get update -y
RUN apt-get upgrade -y

# dockerコンテナ内で日本語が入力できないため設定します。
RUN apt-get install -y locales
RUN locale-gen ja_JP.UTF-8

RUN apt-get install -y vim
RUN apt-get install -y git
RUN apt-get install -y curl
RUN apt-get install -y wget
RUN apt-get install -y iputils-ping
RUN apt-get install -y dotnet-sdk-8.0

USER ubuntu

RUN curl -o- https://raw.githubusercontent.com/nvm-sh/nvm/v0.40.3/install.sh | bash

RUN bash -c "source $HOME/.nvm/nvm.sh && \
nvm install node && \
nvm use node && \
nvm alias default node && \
npm install -g npm \
"

RUN bash -c "source $HOME/.nvm/nvm.sh && npm install -g @anthropic-ai/claude-code"

ENV TERM=xterm-256color
ENV LANG=ja_JP.UTF-8
ENV TZ=Asia/Tokyo

WORKDIR /var/app
