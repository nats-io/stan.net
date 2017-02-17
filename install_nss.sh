#!/bin/sh
set -e
# check to see if nss folder is empty
if [ ! "$(ls -A $HOME/nss)" ]; then
  mkdir -p $HOME/nss;
  cd $HOME/nss
  wget https://github.com/nats-io/nats-streaming-server/releases/download/v0.2.0/nats-streaming-server-linux-amd64.zip -O nss.zip;
  unzip nss.zip;
else
  echo 'Using cached directory.';
fi
