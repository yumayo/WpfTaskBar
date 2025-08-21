FROM ubuntu

RUN apt-get update -y
RUN apt-get upgrade -y

# dockerコンテナ内で日本語が入力できないため設定します。
RUN apt-get install -y locales
RUN locale-gen ja_JP.UTF-8

RUN apt-get install -y sudo
RUN apt-get install -y vim
RUN apt-get install -y git
RUN apt-get install -y curl
RUN apt-get install -y wget
RUN apt-get install -y iputils-ping
RUN apt-get install -y iptables
RUN apt-get install -y ipset
RUN apt-get install -y dnsutils
RUN apt-get install -y jq
RUN apt-get install -y iproute2
RUN apt-get install -y aggregate
RUN apt-get install -y dotnet-sdk-8.0

# ubuntuユーザーにパスワードを設定
RUN echo "ubuntu:ubuntu" | chpasswd

# ubuntuユーザーのホームディレクトリに.hushloginファイルを作成してMOTDを抑制
# .hushloginファイルが無いと下記のメッセージが表示されます。
# To run a command as administrator (user "root"), use "sudo <command>".
# See "man sudo_root" for details.
RUN touch /home/ubuntu/.hushlogin
RUN chown ubuntu:ubuntu /home/ubuntu/.hushlogin

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

# Copy and set up firewall script
COPY ./docker/ai/init-firewall.sh /usr/local/bin/
USER root
RUN chmod +x /usr/local/bin/init-firewall.sh && \
  echo "ubuntu ALL=(root) NOPASSWD: /usr/local/bin/init-firewall.sh" > /etc/sudoers.d/ubuntu-firewall && \
  chmod 0440 /etc/sudoers.d/ubuntu-firewall
USER ubuntu

WORKDIR /var/app
