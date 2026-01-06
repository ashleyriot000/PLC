using UnityEngine;

public class ProductManager : MonoBehaviour
{
    //생산할 제품 프리팹
    public GameObject[] products;
    //생산 위치
    public Transform producePosition;

    //디바이스 주소
    public string address;

    private short prevValue = 0;

    public void Produce(short readValue)
    {
        if (prevValue == readValue)
            return;

        if(readValue > 0)
            Instantiate(products[Random.Range(0, products.Length)], producePosition.position, producePosition.rotation);

        prevValue = readValue;
    }

    private void Start()
    {
        MXRequester.Get.AddDeviceAddress(address, Produce);
    }
}
