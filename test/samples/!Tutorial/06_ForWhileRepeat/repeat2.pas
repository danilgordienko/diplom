// Цикл repeat. Алгоритм Евклида нахождения наибольшего общего делителя
begin
  Print('Введите два целых числа:');
  var A := ReadInteger;
  var B := ReadInteger;
  repeat
    var C := A mod B;
    A := B;
    B := C;
  until B = 0;
  Println('Наибольший общий делитель =', A);
end.
