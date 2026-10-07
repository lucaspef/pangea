-- Base: sequência global de identificadores de objetos do jogo.
-- Todo item, personagem, caddie, card e mascote recebe o seu id desta sequência (regra 11):
-- nunca há dois objetos com o mesmo id, nem entre jogadores e bots.
-- O cliente usa ids de 32 bits com sinal; começamos longe do zero para não colidir com ids fixos.
create sequence object_id_seq as integer start with 1000000 maxvalue 2147483647 no cycle;
