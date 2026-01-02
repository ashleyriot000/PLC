using UnityEngine;

public class ProductManager : MonoBehaviour
{
    //생산할 제품 프리팹
    public GameObject product;
    //생산 위치
    public Transform producePosition;
    //1회 공정당 생산량(-1 : 무한)
    public int produceCount = -1;
    //첫 제품이 나오는데 걸리는 시간
    public float firstMakingTime = 1f;
    //생산 주기
    public float produceInterval = 1f;

    //현재 생산중인지 여부
    private bool _canMaking = false;    
    //다음 생산까지 필요한 시간.
    private float _waitTime = 0f;
    //현재까지 생산한 수량
    private int _count = 0;

    void Update()
    {
        //생산중이 아니면 리턴
        if (!_canMaking)
            return;

        //다음 생산까지 시간이 남았으면 리턴
        if (_waitTime > Time.time)
            return;

        //생산
        Instantiate(product, producePosition.position, producePosition.rotation);
        //다음 생산까지 걸리는 시간 갱신
        _waitTime += produceInterval;

        //생산량이 무한이면 리턴
        if (produceCount < 0)
            return;

        //그게 아니면 생산량 집계
        _count++;
        //최대 생산량 도달시
        if(_count ==  produceCount)
        {
            //생산 중단
            _canMaking = false;
        }
    }

    public void StartProduce()
    {
        if(_canMaking) 
            return;

        _canMaking = true;
        _count = 0;
        _waitTime = Time.time + firstMakingTime;
    }
}
